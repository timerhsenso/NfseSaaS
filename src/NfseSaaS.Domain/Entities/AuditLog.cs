using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Registro de auditoria de operações realizadas dentro de um Tenant.
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
///
/// NUNCA deve conter senha, senha de certificado, chave privada ou
/// qualquer conteúdo sensível — ver <see cref="Dados"/>.
/// </summary>
public sealed class AuditLog : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid? UserId { get; set; }

    public DateTimeOffset DataHora { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Nome da operação realizada (ex.: "EmitirNfse", "CadastrarCliente").</summary>
    public string Operacao { get; set; } = string.Empty;

    /// <summary>Nome da entidade afetada (ex.: "Nfse", "Cliente").</summary>
    public string Entidade { get; set; } = string.Empty;

    public Guid? EntidadeId { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>
    /// Dados adicionais não sensíveis sobre a operação (ex.: JSON com campos
    /// alterados). Nunca armazenar senha, senha de certificado, chave privada
    /// ou XML fiscal completo aqui.
    /// </summary>
    public string? Dados { get; set; }
}
