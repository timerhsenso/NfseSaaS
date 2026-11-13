using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>Andamento do pedido para sair das cópias.</summary>
public enum StatusSaidaCopia
{
    /// <summary>Pedido feito na página; esperando a pessoa confirmar pelo e-mail dela.</summary>
    Pendente = 1,
    /// <summary>Confirmado: o e-mail saiu das cópias do cliente. Fica como histórico e bloqueio.</summary>
    Confirmada = 2
}

/// <summary>
/// RC1: pedido de quem recebe a nota COMO CÓPIA para sair das cópias de um
/// cliente (link no rodapé do e-mail da nota). Só vale com a confirmação
/// enviada para o próprio endereço — ninguém tira outra pessoa. O
/// destinatário principal nunca sai por aqui. Confirmada = histórico e
/// aviso ao recadastrar.
///
/// Entidade TENANT-SCOPED (a conta do fornecedor): a página é pública, então
/// as gravações usam TenantIdOverrideDeSistema.
/// </summary>
public sealed class SaidaCopiaEmail : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid ClienteId { get; set; }

    /// <summary>Nota de onde veio o pedido (o link do rodapé).</summary>
    public Guid? NfseId { get; set; }

    /// <summary>E-mail em minúsculas, sem espaços.</summary>
    public string Email { get; set; } = string.Empty;

    public StatusSaidaCopia Status { get; set; } = StatusSaidaCopia.Pendente;

    /// <summary>SHA-256 do código do link de confirmação (o código em si nunca é guardado).</summary>
    public string CodigoHash { get; set; } = string.Empty;

    /// <summary>Até quando o link de confirmação vale.</summary>
    public DateTimeOffset ExpiraEm { get; set; }

    public DateTimeOffset? ConfirmadoEm { get; set; }

    public string? IpPedido { get; set; }

    public string? IpConfirmacao { get; set; }
}
