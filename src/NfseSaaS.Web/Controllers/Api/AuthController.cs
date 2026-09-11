using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Web.Controllers.Api;

public sealed record RegistrarRequest(string Email, string Senha, string RazaoSocialTenant, string CnpjTenant);

public sealed record LoginRequest(string Email, string Senha);

public sealed record ConvidarRequest(string Email, string Papel);

public sealed record AceitarConviteRequest(string Email, string Token, string NovaSenha);

public sealed record AlterarPapelRequest(string Papel);

/// <summary>Linha da grid de usuários — ver AuthController.ListarUsuarios.</summary>
public sealed record UsuarioResponse(
    Guid Id,
    string Email,
    string Papel,
    DateTimeOffset? ConvidadoEm,
    DateTimeOffset? ConviteAceitoEm,
    bool Bloqueado,
    bool EhVoce)
{
    /// <summary>Convidado mas ainda não aceitou. Falso pro primeiro Administrador de cada Tenant (não passou por convite).</summary>
    public bool Pendente => ConvidadoEm is not null && ConviteAceitoEm is null;
}

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
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IEmailSender _emailSender;
    private readonly EmailOptions _emailOptions;
    private readonly IHostEnvironment _environment;

    public AuthController(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        IAuditLogWriter auditLogWriter,
        IEmailSender emailSender,
        IOptions<EmailOptions> emailOptions,
        IHostEnvironment environment)
    {
        _db = db;
        _userManager = userManager;
        _signInManager = signInManager;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _auditLogWriter = auditLogWriter;
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

        // O primeiro usuário de um Tenant é sempre Administrador — não há
        // ninguém ainda pra convidá-lo com outro papel.
        await _userManager.AddToRoleAsync(user, Papeis.Administrador);

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
    [Authorize(Roles = Papeis.Administrador)]
    [HttpPost("convidar")]
    public async Task<IActionResult> Convidar([FromBody] ConvidarRequest request, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            return Unauthorized();

        if (!Papeis.Todos.Contains(request.Papel))
            return BadRequest(new { erro = $"Papel inválido. Valores aceitos: {string.Join(", ", Papeis.Todos)}." });

        var usuarioExistente = await _userManager.FindByEmailAsync(request.Email);
        if (usuarioExistente is not null)
            return Conflict(new { erro = "Já existe um usuário com este e-mail." });

        var novoUsuario = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            TenantId = tenantId,
            EmailConfirmed = false,
            ConvidadoEm = DateTimeOffset.UtcNow
        };

        // Senha aleatória inicial, nunca usada — o convite só é utilizável
        // através do token de AceitarConvite (ResetPasswordAsync).
        var senhaAleatoriaInicial = Guid.NewGuid().ToString("N") + "Aa1!";
        var resultado = await _userManager.CreateAsync(novoUsuario, senhaAleatoriaInicial);

        if (!resultado.Succeeded)
            return BadRequest(resultado.Errors.Select(e => e.Description));

        await _userManager.AddToRoleAsync(novoUsuario, request.Papel);

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

        // Marca o convite como aceito — é este campo (não EmailConfirmed,
        // que o Identity usa pra outra finalidade) que a listagem de
        // convites usa pra decidir o Status e bloquear reenvio/exclusão.
        usuario.ConviteAceitoEm = DateTimeOffset.UtcNow;
        await _userManager.UpdateAsync(usuario);

        await _signInManager.SignInAsync(usuario, isPersistent: false);

        return Ok();
    }

    /// <summary>
    /// Lista TODOS os usuários do Tenant atual (não só convites pendentes —
    /// inclui quem já aceitou e o próprio Administrador criado em
    /// Registrar), com papel e status calculado (Pendente/Bloqueado/Ativo
    /// — ver UsuarioResponse.Pendente e o campo Bloqueado). É a única
    /// tela de "quem tem acesso ao sistema". AspNetUsers não implementa
    /// ITenantEntity (não sofre o Global Query Filter), então o filtro
    /// por tenant aqui é manual, de propósito.
    /// </summary>
    [Authorize(Roles = Papeis.Administrador)]
    [HttpGet("usuarios")]
    public async Task<IActionResult> ListarUsuarios(CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            return Unauthorized();

        var agora = DateTimeOffset.UtcNow;

        var usuarios = await (
            from u in _db.Users.AsNoTracking()
            where u.TenantId == tenantId
            join ur in _db.UserRoles.AsNoTracking() on u.Id equals ur.UserId into userRoles
            from ur in userRoles.DefaultIfEmpty()
            join r in _db.Roles.AsNoTracking() on ur.RoleId equals r.Id into roles
            from r in roles.DefaultIfEmpty()
            orderby u.Email
            select new UsuarioResponse(
                u.Id,
                u.Email!,
                r != null ? r.Name! : "-",
                u.ConvidadoEm,
                u.ConviteAceitoEm,
                u.LockoutEnd != null && u.LockoutEnd > agora,
                u.Id == _currentUser.UserId)
        ).ToListAsync(cancellationToken);

        return Ok(usuarios);
    }

    /// <summary>
    /// Troca o papel de um usuário existente (remove o(s) papel(is) atual(is)
    /// e atribui o novo — um usuário sempre tem exatamente um papel neste
    /// sistema, ver Convidar). Bloqueado se o usuário for o único
    /// Administrador do Tenant (ver EhUnicoAdministradorAsync) — senão o
    /// próprio Tenant ficaria sem ninguém pra gerenciar usuários.
    /// </summary>
    [Authorize(Roles = Papeis.Administrador)]
    [HttpPut("usuarios/{id:guid}/papel")]
    public async Task<IActionResult> AlterarPapel(Guid id, [FromBody] AlterarPapelRequest request, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            return Unauthorized();

        if (!Papeis.Todos.Contains(request.Papel))
            return BadRequest(new { erro = $"Papel inválido. Valores aceitos: {string.Join(", ", Papeis.Todos)}." });

        var usuario = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, cancellationToken);
        if (usuario is null)
            return NotFound();

        var papeisAtuais = await _userManager.GetRolesAsync(usuario);
        var papelAtual = papeisAtuais.FirstOrDefault();

        if (papelAtual == request.Papel)
            return NoContent();

        if (papelAtual == Papeis.Administrador && await EhUnicoAdministradorAsync(tenantId, usuario.Id, cancellationToken))
            return Conflict(new { erro = "Este é o único Administrador do Tenant — promova outro usuário antes de trocar o papel dele." });

        if (papeisAtuais.Count > 0)
            await _userManager.RemoveFromRolesAsync(usuario, papeisAtuais);

        await _userManager.AddToRoleAsync(usuario, request.Papel);

        _auditLogWriter.Registrar("AlterarPapelUsuario", "ApplicationUser", usuario.Id, new { usuario.Email, papelAnterior = papelAtual, papelNovo = request.Papel });
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Bloqueia o login do usuário (Lockout do próprio Identity, sem
    /// excluir a conta — LockoutEnabled já vem true por padrão em todo
    /// usuário criado via CreateAsync). Não permite bloquear a própria
    /// conta nem o único Administrador do Tenant.
    /// </summary>
    [Authorize(Roles = Papeis.Administrador)]
    [HttpPost("usuarios/{id:guid}/bloquear")]
    public async Task<IActionResult> Bloquear(Guid id, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            return Unauthorized();

        if (_currentUser.UserId == id)
            return Conflict(new { erro = "Não é possível bloquear a própria conta." });

        var usuario = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, cancellationToken);
        if (usuario is null)
            return NotFound();

        if (await EhUnicoAdministradorAsync(tenantId, usuario.Id, cancellationToken))
            return Conflict(new { erro = "Este é o único Administrador do Tenant — não é possível bloqueá-lo." });

        await _userManager.SetLockoutEndDateAsync(usuario, DateTimeOffset.MaxValue);

        _auditLogWriter.Registrar("BloquearUsuario", "ApplicationUser", usuario.Id, new { usuario.Email });
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Remove o bloqueio de login (ver Bloquear).</summary>
    [Authorize(Roles = Papeis.Administrador)]
    [HttpPost("usuarios/{id:guid}/desbloquear")]
    public async Task<IActionResult> Desbloquear(Guid id, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            return Unauthorized();

        var usuario = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, cancellationToken);
        if (usuario is null)
            return NotFound();

        await _userManager.SetLockoutEndDateAsync(usuario, null);

        _auditLogWriter.Registrar("DesbloquearUsuario", "ApplicationUser", usuario.Id, new { usuario.Email });
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Reenvia o e-mail de convite (novo token — o anterior continua
    /// válido também, o Identity não invalida tokens antigos ao gerar um
    /// novo). Só funciona pra convite ainda pendente.
    /// </summary>
    [Authorize(Roles = Papeis.Administrador)]
    [HttpPost("usuarios/{id:guid}/reenviar-convite")]
    public async Task<IActionResult> ReenviarConvite(Guid id, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            return Unauthorized();

        var usuario = await _db.Users.FirstOrDefaultAsync(
            u => u.Id == id && u.TenantId == tenantId && u.ConvidadoEm != null, cancellationToken);
        if (usuario is null)
            return NotFound();

        if (usuario.ConviteAceitoEm is not null)
            return Conflict(new { erro = "Este convite já foi aceito — não é possível reenviar." });

        var token = await _userManager.GeneratePasswordResetTokenAsync(usuario);

        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        var emailEnviado = await EnviarEmailDeConviteAsync(usuario.Email!, token, tenant?.RazaoSocial, cancellationToken);

        var resposta = new Dictionary<string, object?>
        {
            ["userId"] = usuario.Id,
            ["email"] = usuario.Email,
            ["emailEnviado"] = emailEnviado
        };

        if (_environment.IsDevelopment() || !emailEnviado)
            resposta["token"] = token;

        return Ok(resposta);
    }

    /// <summary>
    /// Exclusão REAL de um usuário — funciona tanto pra convite ainda
    /// pendente quanto pra usuário já ativo (nenhuma tabela de negócio
    /// tem FK pra AspNetUsers — AuditLog.UserId é um Guid solto, sem
    /// relação configurada — então não há histórico bloqueando isto).
    /// Não permite excluir a própria conta nem o único Administrador do
    /// Tenant.
    /// </summary>
    [Authorize(Roles = Papeis.Administrador)]
    [HttpDelete("usuarios/{id:guid}")]
    public async Task<IActionResult> ExcluirUsuario(Guid id, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            return Unauthorized();

        if (_currentUser.UserId == id)
            return Conflict(new { erro = "Não é possível excluir a própria conta." });

        var usuario = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, cancellationToken);
        if (usuario is null)
            return NotFound();

        if (await EhUnicoAdministradorAsync(tenantId, usuario.Id, cancellationToken))
            return Conflict(new { erro = "Este é o único Administrador do Tenant — não é possível excluí-lo." });

        var email = usuario.Email;
        var resultado = await _userManager.DeleteAsync(usuario);
        if (!resultado.Succeeded)
            return BadRequest(resultado.Errors.Select(e => e.Description));

        _auditLogWriter.Registrar("ExcluirUsuario", "ApplicationUser", id, new { email });
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// True se o usuário informado for Administrador e não houver
    /// nenhum outro Administrador no mesmo Tenant — usado pra bloquear
    /// ações (trocar papel, bloquear, excluir) que deixariam o Tenant
    /// sem ninguém capaz de gerenciar usuários.
    /// </summary>
    private async Task<bool> EhUnicoAdministradorAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken)
    {
        var papelAdministrador = await _db.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Name == Papeis.Administrador, cancellationToken);
        if (papelAdministrador is null)
            return false;

        var ehAdministrador = await _db.UserRoles.AsNoTracking()
            .AnyAsync(ur => ur.UserId == usuarioId && ur.RoleId == papelAdministrador.Id, cancellationToken);
        if (!ehAdministrador)
            return false;

        var totalAdministradoresDoTenant = await (
            from u in _db.Users.AsNoTracking()
            join ur in _db.UserRoles.AsNoTracking() on u.Id equals ur.UserId
            where u.TenantId == tenantId && ur.RoleId == papelAdministrador.Id
            select u.Id
        ).CountAsync(cancellationToken);

        return totalAdministradoresDoTenant <= 1;
    }

    private async Task<bool> EnviarEmailDeConviteAsync(string destinatarioEmail, string token, string? razaoSocialTenant, CancellationToken cancellationToken)
    {
        var nomeExibicao = string.IsNullOrWhiteSpace(razaoSocialTenant) ? "NfseSaaS" : razaoSocialTenant;

        var linkAceite = string.IsNullOrWhiteSpace(_emailOptions.AppBaseUrl)
            ? null
            : $"{_emailOptions.AppBaseUrl.TrimEnd('/')}/Account/AceitarConvite?email={Uri.EscapeDataString(destinatarioEmail)}&token={Uri.EscapeDataString(token)}";

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
