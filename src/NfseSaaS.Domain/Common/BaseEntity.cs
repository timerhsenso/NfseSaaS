namespace NfseSaaS.Domain.Common;

/// <summary>
/// Classe base para entidades do domínio. Define identidade e metadados
/// de auditoria comuns (criação/atualização), sempre em UTC.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}
