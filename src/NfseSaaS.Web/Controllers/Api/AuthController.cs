using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Web.Controllers.Api;

public sealed record RegistrarRequest(string Email, string Senha, string RazaoSocialTenant, string CnpjTenant);

public sealed record LoginRequest(string Email, string Senha);

/// <summary>
/// Autenticação e bootstrap de Tenant. Fluxo mínimo (sem convite, sem
/// hierarquia de permissões) — cada registro cria um novo Tenant com um
/// único usuário administrador. Convites para adicionar mais usuários ao
/// mesmo Tenant ficam para uma fase futura.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public AuthController(AppDbContext db, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
    {
        _db = db;
        _userManager = userManager;
        _signInManager = signInManager;
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
}
