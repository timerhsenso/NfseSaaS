using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using NfseSaaS.Infrastructure.MultiTenancy;

namespace NfseSaaS.Infrastructure.Identity;

/// <summary>
/// Estende os Claims padrão do ASP.NET Core Identity com "tenant_id" — é
/// esse Claim que ICurrentTenant/CurrentTenant usa para resolver o Tenant
/// da requisição autenticada (ver MultiTenancy/CurrentTenant.cs). Roda
/// automaticamente sempre que o Identity gera o cookie de autenticação
/// (login, registro) — mecanismo de extensão padrão do próprio Identity,
/// não uma customização por fora dele.
/// </summary>
public sealed class ApplicationUserClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>
{
    public ApplicationUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(CurrentTenant.TenantIdClaimType, user.TenantId.ToString()));
        return identity;
    }
}
