using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Domain.Entities;
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
/// há geração de token própria. Como o envio de e-mail está fora do
/// escopo desta fase, o token retorna na resposta da API e precisa ser
/// repassado manualmente à pessoa convidada; isso muda no dia em que o
/// envio de e-mail for implementado, sem alterar a lógica de convite.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ICurrentTenant _currentTenant;

    public AuthController(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ICurrentTenant currentTenant)
    {
        _db = db;
        _userManager = userManager;
        _signInManager = signInManager;
        _currentTenant = currentTenant;
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
    /// Cria a conta (sem senha utilizável) e devolve um token de convite —
    /// a pessoa convidada usa esse token em AceitarConvite para definir a
    /// própria senha e ficar autenticada.
    /// </summary>
    [Authorize]
    [HttpPost("convidar")]
    public async Task<IActionResult> Convidar([FromBody] ConvidarRequest request)
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

        return Ok(new { userId = novoUsuario.Id, email = novoUsuario.Email, token });
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
}