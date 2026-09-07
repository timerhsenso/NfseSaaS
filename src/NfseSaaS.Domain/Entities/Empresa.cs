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

    public string Telefone { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    // --- Regime tributário (necessário para montar a DPS — ver <regTrib> e <trib> no layout da NFS-e Nacional) ---

    /// <summary>Opção pelo Simples Nacional ("1" Não optante, "2" Optante MEI, "3" Optante Simples Nacional exceto MEI).</summary>
    public string OpSimpNac { get; set; } = string.Empty;

    public string RegApTribSN { get; set; } = string.Empty;

    public string RegEspTrib { get; set; } = string.Empty;

    public string TribIssqn { get; set; } = string.Empty;

    public string TpRetIssqn { get; set; } = string.Empty;

    public string CstPisCofins { get; set; } = string.Empty;

    public string TpRetPisCofins { get; set; } = string.Empty;

    /// <summary>Percentual total de tributos do Simples Nacional (campo pTotTribSN da DPS), ex.: "3.00".</summary>
    public string PercentualTotalTributosSimplesNacional { get; set; } = string.Empty;

    public bool Ativo { get; set; } = true;
}
