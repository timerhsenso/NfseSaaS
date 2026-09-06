using Microsoft.AspNetCore.Identity;

namespace NfseSaaS.Infrastructure.Identity;

/// <summary>
/// Usuário da aplicação. Estende o Identity padrão com o TenantId ao qual
/// o usuário pertence — futuramente incluído nos Claims no login para
/// resolução do ICurrentTenant (ver NfseSaaS.Infrastructure.MultiTenancy).
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
}
