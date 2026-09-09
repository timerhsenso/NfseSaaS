using NfseSaaS.Domain.Common;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Um evento no ciclo de vida de uma Nfse (DPS enviada, autorizada,
/// rejeitada, cancelada, cancelamento rejeitado etc.). Existe porque
/// guardar só o Status atual da Nfse dificulta suporte e diagnóstico —
/// uma nota rejeitada e depois corrigida/reenviada, por exemplo, não
/// deixa rastro de qual foi o erro original sem isto.
///
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
///
/// Deliberadamente NÃO duplica XmlDps/XmlNfse aqui — esses já ficam
/// persistidos na própria Nfse; um evento aponta pra eles pelo NfseId,
/// não guarda cópia do payload.
/// </summary>
public sealed class NfseEvento : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid NfseId { get; set; }

    public NfseEventoTipo Tipo { get; set; }

    /// <summary>Código de erro/rejeição retornado pela SEFIN, quando houver (ex.: "E0008").</summary>
    public string? Codigo { get; set; }

    public string? Mensagem { get; set; }

    // DataHora do evento é o próprio CreatedAt herdado de BaseEntity —
    // não duplicado aqui como campo separado.
}
