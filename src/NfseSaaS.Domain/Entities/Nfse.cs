using NfseSaaS.Domain.Common;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Representa uma NFS-e (e a DPS que a originou) dentro do SaaS.
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
/// </summary>
public sealed class Nfse : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmpresaId { get; set; }

    public Guid ClienteId { get; set; }

    public int NumeroDps { get; set; }

    public string SerieDps { get; set; } = string.Empty;

    /// <summary>Número da NFS-e retornado pela SEFIN após autorização (nulo até então).</summary>
    public string? NumeroNfse { get; set; }

    /// <summary>Chave de acesso retornada pela SEFIN após autorização (nulo até então).</summary>
    public string? ChaveAcesso { get; set; }

    public DateOnly DataCompetencia { get; set; }

    public DateTimeOffset? DataEmissao { get; set; }

    public decimal ValorServico { get; set; }

    public string DescricaoServico { get; set; } = string.Empty;

    public NfseStatus Status { get; set; } = NfseStatus.Rascunho;

    /// <summary>XML da DPS gerada e assinada, enviada à SEFIN.</summary>
    public string? XmlDps { get; set; }

    /// <summary>XML da NFS-e retornado pela SEFIN após autorização.</summary>
    public string? XmlNfse { get; set; }

    /// <summary>Código do erro/rejeição retornado pela SEFIN, quando houver (ex.: "E0008").</summary>
    public string? CodigoErro { get; set; }

    public string? MensagemErro { get; set; }
}
