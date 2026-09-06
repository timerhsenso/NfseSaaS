using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NfseSaaS.Application.Abstractions;

namespace NfseSaaS.Infrastructure.MultiTenancy;

/// <summary>
/// Implementação de ICurrentTenant baseada no HttpContext da requisição atual.
/// Resolve o TenantId EXCLUSIVAMENTE a partir dos Claims do usuário
/// autenticado (claim "tenant_id") — nunca de query string, header ou body,
/// para impedir que um usuário do Tenant A se passe pelo Tenant B alterando
/// um valor no cliente.
/// </summary>
public sealed class CurrentTenant : ICurrentTenant
{
    public const string TenantIdClaimType = "tenant_id";

    private readonly Guid? _tenantId;

    public CurrentTenant(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User;
        var claimValue = user?.FindFirstValue(TenantIdClaimType);

        _tenantId = Guid.TryParse(claimValue, out var tenantId) ? tenantId : null;
    }

    public Guid? TenantId => _tenantId;

    public bool IsResolved => _tenantId.HasValue;
}
