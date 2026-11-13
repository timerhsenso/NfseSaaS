using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// CR1: um destinatário do e-mail da nota (principal ou cópia) confirmou que
/// recebeu, pelo link do campo [Confirmar recebimento]. Uma linha por e-mail
/// e nota; o ícone da lista fica verde com a primeira.
///
/// Entidade TENANT-SCOPED (a conta do fornecedor): a página é pública, então
/// a gravação usa TenantIdOverrideDeSistema.
/// </summary>
public sealed class ConfirmacaoRecebimentoNota : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid NfseId { get; set; }

    /// <summary>E-mail em minúsculas, conferido contra os destinatários dos envios da nota.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>True = era o destinatário principal; false = uma das cópias.</summary>
    public bool Principal { get; set; }

    public string? Ip { get; set; }

    public DateTimeOffset ConfirmadoEm { get; set; }
}
