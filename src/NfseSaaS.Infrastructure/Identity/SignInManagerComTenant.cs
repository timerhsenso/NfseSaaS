using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Identity;

/// <summary>
/// Login só para usuário de conta (Tenant) ATIVA. Conta suspensa pelo
/// operador da plataforma → PasswordSignInAsync devolve NotAllowed (o
/// AuthController traduz para "conta suspensa"). O operador nunca é
/// barrado, para não se trancar fora por engano. Quem já estava logado cai
/// em até 1 minuto: suspender troca o SecurityStamp de todos os usuários
/// da conta (ver AlterarBloqueioTenantUseCase).
/// </summary>
public sealed class SignInManagerComTenant : SignInManager<ApplicationUser>
{
    private readonly AppDbContext _db;
    private readonly PlataformaOptions _plataforma;

    public SignInManagerComTenant(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<ApplicationUser>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<ApplicationUser> confirmation,
        AppDbContext db,
        IOptions<PlataformaOptions> plataforma)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
        _db = db;
        _plataforma = plataforma.Value;
    }

    public override async Task<bool> CanSignInAsync(ApplicationUser user)
    {
        if (!await base.CanSignInAsync(user))
            return false;

        if (_plataforma.UsuariosOperadoresIds.Contains(user.Id))
            return true;

        var ativo = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == user.TenantId)
            .Select(t => (bool?)t.Ativo)
            .FirstOrDefaultAsync();

        if (ativo == false)
        {
            Logger.LogInformation("Login recusado: usuário {UserId} pertence à conta suspensa {TenantId}.", user.Id, user.TenantId);
            return false;
        }

        return true;
    }
}
