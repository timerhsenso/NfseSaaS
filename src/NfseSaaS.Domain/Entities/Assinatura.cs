using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Common;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Assinatura de uma conta (Tenant) no micoempresa. Uma por conta, criada na
/// primeira vez que a cobrança é consultada. Entidade GLOBAL (como o Tenant):
/// o webhook do Asaas chega sem usuário logado e precisa achá-la pelo id da
/// assinatura, então o acesso é sempre explícito por TenantId.
/// </summary>
public sealed class Assinatura : BaseEntity
{
    public Guid TenantId { get; set; }

    public StatusAssinatura Status { get; set; } = StatusAssinatura.Teste;

    /// <summary>Código do plano escolhido ao assinar (Plano.Codigo; nulo enquanto em teste).</summary>
    public int? Plano { get; set; }

    /// <summary>Valor mensal contratado (preço de fundador fica gravado aqui).</summary>
    public decimal? PrecoMensal { get; set; }

    public bool Fundador { get; set; }

    /// <summary>
    /// Início do teste grátis em produção: a primeira nota emitida em
    /// produção. Nulo = ainda não usou produção (homologação não conta).
    /// </summary>
    public DateTimeOffset? TesteIniciadoEm { get; set; }

    public string? AsaasClienteId { get; set; }

    public string? AsaasAssinaturaId { get; set; }

    public DateTimeOffset? AssinadaEm { get; set; }

    /// <summary>Vencimento da fatura em atraso (nulo quando em dia).</summary>
    public DateOnly? AtrasadaDesde { get; set; }

    public DateTimeOffset? UltimoPagamentoEm { get; set; }

    public DateTimeOffset? CanceladaEm { get; set; }

    /// <summary>Link da fatura em aberto (Pix, boleto ou cartão), para o botão "Pagar".</summary>
    public string? UrlFaturaPendente { get; set; }
}
