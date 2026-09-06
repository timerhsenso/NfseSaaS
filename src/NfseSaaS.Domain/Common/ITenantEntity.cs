namespace NfseSaaS.Domain.Common;

/// <summary>
/// Marca uma entidade como pertencente a um Tenant (empresa cliente do SaaS).
/// Entidades que implementam esta interface são automaticamente filtradas
/// por TenantId através dos Global Query Filters configurados no
/// NfseSaaS.Infrastructure.Persistence.AppDbContext — não é necessário (nem
/// deve ser feito) filtrar manualmente por TenantId nas consultas.
/// </summary>
public interface ITenantEntity
{
    Guid TenantId { get; set; }
}
