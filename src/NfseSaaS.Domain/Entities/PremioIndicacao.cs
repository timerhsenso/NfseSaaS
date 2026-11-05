using NfseSaaS.Domain.Common;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Prêmio do "Indique e ganhe": 1 mês grátis. Quando uma conta indicada paga
/// a primeira fatura, nascem DOIS prêmios (um para ela, um para quem
/// indicou). O par (IndicadoTenantId, Papel) é único: cada indicação premia
/// cada lado uma única vez, mesmo com evento repetido do Asaas.
/// Entidade GLOBAL (como Assinatura): criada pelo webhook e aplicada por job,
/// sem usuário logado; o acesso é sempre explícito por TenantId.
/// </summary>
public sealed class PremioIndicacao : BaseEntity
{
    /// <summary>Conta que recebe o mês grátis.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Conta indicada cujo primeiro pagamento gerou o prêmio.</summary>
    public Guid IndicadoTenantId { get; set; }

    public PapelPremioIndicacao Papel { get; set; }

    public StatusPremioIndicacao Status { get; set; } = StatusPremioIndicacao.Pendente;

    /// <summary>Pagamento do Asaas (primeira fatura da conta indicada) que gerou o prêmio.</summary>
    public string? AsaasPagamentoOrigemId { get; set; }

    /// <summary>Tentativas de aplicar no Asaas (recusas do Asaas; ao chegar no limite vira Erro).</summary>
    public int Tentativas { get; set; }

    public DateTimeOffset? AplicadoEm { get; set; }

    /// <summary>Fatura do Asaas removida como mês grátis (nulo quando o vencimento foi adiado).</summary>
    public string? AsaasPagamentoDispensadoId { get; set; }

    /// <summary>Vencimento que deixou de ser cobrado.</summary>
    public DateOnly? VencimentoDispensado { get; set; }

    /// <summary>O que foi feito, por que está esperando ou o erro (para o operador).</summary>
    public string? Detalhe { get; set; }
}
