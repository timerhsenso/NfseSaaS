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
///
/// IMPORTANTE: o Claim é lido a cada acesso à propriedade, não uma única
/// vez no construtor. O ASP.NET Core Identity valida o cookie (checagem de
/// "security stamp") internamente durante o próprio processamento da
/// autenticação, e essa validação pode acabar resolvendo AppDbContext (e,
/// por consequência, esta classe, injetada no seu construtor) ANTES de
/// HttpContext.User estar totalmente populado com a identidade final da
/// requisição. Como este serviço é Scoped (uma instância por requisição),
/// resolver o TenantId uma única vez no construtor arriscava "congelar" um
/// valor nulo/desatualizado pelo resto da requisição.
/// </summary>
public sealed class CurrentTenant : ICurrentTenant
{
    public const string TenantIdClaimType = "tenant_id";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentTenant(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? TenantId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var claimValue = user?.FindFirstValue(TenantIdClaimType);

            return Guid.TryParse(claimValue, out var tenantId) ? tenantId : null;
        }
    }

    public bool IsResolved => TenantId.HasValue;
}