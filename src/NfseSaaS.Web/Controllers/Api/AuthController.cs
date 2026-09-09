using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Web.Controllers.Api;

public sealed record RegistrarRequest(string Email, string Senha, string RazaoSocialTenant, string CnpjTenant);

public sealed record LoginRequest(string Email, string Senha);

public sealed record ConvidarRequest(string Email);

public sealed record AceitarConviteRequest(string Email, string Token, string NovaSenha);

/// <summary>
/// Autenticação, bootstrap de Tenant e convite de usuários para o mesmo
/// Tenant. O primeiro registro de cada Tenant cria a conta administradora;
/// usuários adicionais entram via convite (ver Convidar/AceitarConvite).
///
/// Convite reaproveita o mecanismo de token de redefinição de senha do
/// próprio ASP.NET Core Identity (o mesmo de "esqueci minha senha") — não
/// há geração de token própria.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly IEmailSender _emailSender;
    private readonly EmailOptions _emailOptions;
    private readonly IHostEnvironment _environment;

    public AuthController(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ICurrentTenant currentTenant,
        IEmailSender emailSender,
        IOptions<EmailOptions> emailOptions,
        IHostEnvironment environment)
    {
        _db = db;
        _userManager = userManager;
        _signInManager = signInManager;
        _currentTenant = currentTenant;
        _emailSender = emailSender;
        _emailOptions = emailOptions.Value;
        _environment = environment;
    }

    /// <summary>Cria um novo Tenant e seu primeiro usuário, já autenticado (cookie com o Claim tenant_id).</summary>
    [HttpPost("registrar")]
    public async Task<IActionResult> Registrar([FromBody] RegistrarRequest request, CancellationToken cancellationToken)
    {
        var tenant = new Tenant
        {
            Cnpj = request.CnpjTenant,
            RazaoSocial = request.RazaoSocialTenant
        };

        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(cancellationToken);

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            TenantId = tenant.Id
        };

        var resultado = await _userManager.CreateAsync(user, request.Senha);

        if (!resultado.Succeeded)
            return BadRequest(resultado.Errors.Select(e => e.Description));

        await _signInManager.SignInAsync(user, isPersistent: false);

        return Ok(new { tenantId = tenant.Id, userId = user.Id });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var resultado = await _signInManager.PasswordSignInAsync(
            request.Email, request.Senha, isPersistent: false, lockoutOnFailure: false);

        return resultado.Succeeded ? Ok() : Unauthorized();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return Ok();
    }

    /// <summary>
    /// Convida um e-mail para entrar no MESMO Tenant do usuário autenticado.
    /// Cria a conta (sem senha utilizável), gera um token de convite e
    /// tenta enviá-lo por e-mail. Se o envio falhar (ou em ambiente de
    /// Development), o token também volta na resposta da API — nunca
    /// deixamos o convite sem nenhuma forma de ser completado.
    /// </summary>
    [Authorize]
    [HttpPost("convidar")]
    public async Task<IActionResult> Convidar([FromBody] ConvidarRequest request, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            return Unauthorized();

        var usuarioExistente = await _userManager.FindByEmailAsync(request.Email);
        if (usuarioExistente is not null)
            return Conflict(new { erro = "Já existe um usuário com este e-mail." });

        var novoUsuario = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            TenantId = tenantId,
            EmailConfirmed = false
        };

        // Senha aleatória inicial, nunca usada — o convite só é utilizável
        // através do token de AceitarConvite (ResetPasswordAsync).
        var senhaAleatoriaInicial = Guid.NewGuid().ToString("N") + "Aa1!";
        var resultado = await _userManager.CreateAsync(novoUsuario, senhaAleatoriaInicial);

        if (!resultado.Succeeded)
            return BadRequest(resultado.Errors.Select(e => e.Description));

        var token = await _userManager.GeneratePasswordResetTokenAsync(novoUsuario);

        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        var emailEnviado = await EnviarEmailDeConviteAsync(request.Email, token, tenant?.RazaoSocial, cancellationToken);

        var resposta = new Dictionary<string, object?>
        {
            ["userId"] = novoUsuario.Id,
            ["email"] = novoUsuario.Email,
            ["emailEnviado"] = emailEnviado
        };

        // Sem o token aqui, um e-mail que falhou vira um convite sem
        // nenhuma forma de ser completado — então ele SEMPRE volta quando
        // o envio não deu certo, mesmo em produção. Em Development volta
        // sempre, pra testar via Swagger sem precisar de SMTP configurado.
        if (_environment.IsDevelopment() || !emailEnviado)
            resposta["token"] = token;

        return Ok(resposta);
    }

    /// <summary>Aceita um convite: define a senha da conta criada em Convidar (via token) e já autentica.</summary>
    [AllowAnonymous]
    [HttpPost("aceitar-convite")]
    public async Task<IActionResult> AceitarConvite([FromBody] AceitarConviteRequest request)
    {
        var usuario = await _userManager.FindByEmailAsync(request.Email);
        if (usuario is null)
            return NotFound();

        var resultado = await _userManager.ResetPasswordAsync(usuario, request.Token, request.NovaSenha);
        if (!resultado.Succeeded)
            return BadRequest(resultado.Errors.Select(e => e.Description));

        await _signInManager.SignInAsync(usuario, isPersistent: false);

        return Ok();
    }

    private async Task<bool> EnviarEmailDeConviteAsync(string destinatarioEmail, string token, string? razaoSocialTenant, CancellationToken cancellationToken)
    {
        var nomeExibicao = string.IsNullOrWhiteSpace(razaoSocialTenant) ? "NfseSaaS" : razaoSocialTenant;

        var linkAceite = string.IsNullOrWhiteSpace(_emailOptions.AppBaseUrl)
            ? null
            : $"{_emailOptions.AppBaseUrl.TrimEnd('/')}/aceitar-convite?email={Uri.EscapeDataString(destinatarioEmail)}&token={Uri.EscapeDataString(token)}";

        var corpoHtml = $"""
            <p>Você foi convidado para acessar o <strong>{WebUtility.HtmlEncode(nomeExibicao)}</strong> no NfseSaaS.</p>
            {(linkAceite is not null ? $"""<p><a href="{linkAceite}">Clique aqui para definir sua senha e aceitar o convite</a></p>""" : "")}
            <p>Se o link acima não abrir uma tela (ou se preferir usar via API), utilize estes dados no aceite do convite:</p>
            <ul>
                <li>E-mail: {WebUtility.HtmlEncode(destinatarioEmail)}</li>
                <li>Token: {WebUtility.HtmlEncode(token)}</li>
            </ul>
            """;

        return await _emailSender.EnviarAsync(destinatarioEmail, $"Convite — {nomeExibicao}", corpoHtml, cancellationToken);
    }
}
