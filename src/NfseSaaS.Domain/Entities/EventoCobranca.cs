using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Evento recebido do webhook do Asaas, gravado ANTES de processar. O id do
/// evento é único: o Asaas entrega "pelo menos uma vez", e um evento repetido
/// não pode aplicar a regra de novo. Entidade global (sem usuário logado).
/// </summary>
public sealed class EventoCobranca : BaseEntity
{
    /// <summary>id do evento no Asaas (ex.: "evt_05b7...&amp;368604920").</summary>
    public string EventoId { get; set; } = string.Empty;

    /// <summary>Tipo (ex.: PAYMENT_RECEIVED).</summary>
    public string Tipo { get; set; } = string.Empty;

    public string? AsaasPagamentoId { get; set; }

    public string? AsaasAssinaturaId { get; set; }

    /// <summary>Conta a que o evento se refere, quando encontrada.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Corpo recebido, como veio (para conferência e reprocessamento).</summary>
    public string Payload { get; set; } = string.Empty;

    public bool Processado { get; set; }

    /// <summary>O que foi feito, ou por que foi ignorado.</summary>
    public string? Resultado { get; set; }
}
