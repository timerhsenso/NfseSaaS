using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Empresa emissora de NFS-e dentro de um Tenant (o SaaS é multiempresa:
/// um mesmo Tenant pode ter mais de uma Empresa/filial emitindo notas,
/// cada uma com seu próprio certificado digital e inscrição municipal).
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
/// </summary>
public sealed class Empresa : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public string Cnpj { get; set; } = string.Empty;

    public string RazaoSocial { get; set; } = string.Empty;

    public string? NomeFantasia { get; set; }

    public string InscricaoMunicipal { get; set; } = string.Empty;

    /// <summary>Código do município (IBGE) onde a empresa está estabelecida.</summary>
    public string CodigoMunicipio { get; set; } = string.Empty;

    public bool Ativo { get; set; } = true;
}
