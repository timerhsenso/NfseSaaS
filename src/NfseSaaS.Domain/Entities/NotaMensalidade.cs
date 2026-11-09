using NfseSaaS.Domain.Common;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// NFS-e da mensalidade (N1): uma por pagamento confirmado no Asaas. A nota
/// em si é uma Nfse comum da conta EMISSORA (a LFMS, configurada em
/// NotaMensalidade:EmpresaId); esta linha liga o pagamento à nota e garante
/// que cada pagamento gere uma nota só. Entidade GLOBAL (como
/// EventoCobranca): TenantId é o da conta que PAGOU, sem FK (a linha
/// sobrevive à exclusão da conta, como o registro fiscal).
/// </summary>
public sealed class NotaMensalidade : BaseEntity
{
    /// <summary>Conta que pagou (o tomador da nota).</summary>
    public Guid TenantId { get; set; }

    /// <summary>id do pagamento no Asaas (pay_...). Único.</summary>
    public string AsaasPagamentoId { get; set; } = string.Empty;

    public decimal Valor { get; set; }

    /// <summary>Dia do pagamento (Brasília) — vira a competência da nota.</summary>
    public DateOnly DataPagamento { get; set; }

    public StatusNotaMensalidade Status { get; set; } = StatusNotaMensalidade.Pendente;

    /// <summary>A nota na conta emissora (nula até a primeira tentativa).</summary>
    public Guid? NfseId { get; set; }

    public int Tentativas { get; set; }

    public string? UltimoErro { get; set; }

    public DateTimeOffset? EmitidaEm { get; set; }

    /// <summary>Pagamento desfeito (estorno/chargeback) DEPOIS da nota: o operador foi avisado para cancelar a nota.</summary>
    public DateTimeOffset? EstornoAvisadoEm { get; set; }
}
