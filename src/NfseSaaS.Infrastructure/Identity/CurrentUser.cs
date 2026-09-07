using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NfseSaaS.Application.Abstractions;

namespace NfseSaaS.Infrastructure.Identity;

/// <summary>
/// Implementação de ICurrentUser baseada no HttpContext da requisição
/// atual. UserId vem do claim padrão do ASP.NET Core Identity
/// (ClaimTypes.NameIdentifier, adicionado automaticamente pelo próprio
/// UserClaimsPrincipalFactory — não é um claim customizado como
/// "tenant_id"). Mesmo cuidado do CurrentTenant: lido a cada acesso, não
/// uma única vez no construtor.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var claimValue = user?.FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(claimValue, out var userId) ? userId : null;
        }
    }

    public string? IpAddress => _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
