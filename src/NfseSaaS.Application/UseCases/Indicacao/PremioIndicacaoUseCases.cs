using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Indicacao;

/// <summary>
/// Regras do prêmio do "Indique e ganhe" (1 mês grátis para cada lado),
/// sem banco nem gateway, testáveis isoladas:
/// - nasce no PRIMEIRO pagamento confirmado de uma conta que veio por indicação;
/// - só é aplicado em assinatura em dia (Ativa) e com assinatura no Asaas;
///   antes disso fica guardado (quem indicou e ainda está no beta/teste
///   ganha o mês quando passar a pagar);
/// - recusa do Asaas tenta de novo até MaxTentativas; depois, operador.
/// </summary>
public static class RegrasPremioIndicacao
{
    public const int MaxTentativas = 3;

    public const string MotivoIsenta = "Conta isenta de cobrança: não há fatura para dispensar.";
    public const string MotivoSuspensa = "Conta suspensa: o prêmio fica guardado até a reativação.";
    public const string MotivoSemAssinatura = "Guardado: vale quando a conta assinar e pagar a primeira fatura.";
    public const string MotivoAtrasada = "Guardado: vale quando a fatura em atraso for paga.";
    public const string MotivoCancelada = "Guardado: a assinatura está cancelada; vale se a conta assinar de novo.";

    /// <summary>O pagamento confirmado agora gera prêmio de indicação?</summary>
    public static bool GeraPremio(bool primeiroPagamento, Guid tenantId, Guid? indicadoPorTenantId) =>
        primeiroPagamento && indicadoPorTenantId is { } indicador && indicador != tenantId;

    /// <summary>Por que o prêmio ainda não pode ser aplicado (nulo = pode aplicar agora).</summary>
    public static string? MotivoEspera(StatusAssinatura? status, bool temAssinaturaNoGateway, bool isenta, bool contaAtiva)
    {
        if (isenta)
            return MotivoIsenta;
        if (!contaAtiva)
            return MotivoSuspensa;
        if (status == StatusAssinatura.Cancelada)
            return MotivoCancelada;
        if (!temAssinaturaNoGateway || status is null or StatusAssinatura.Teste)
            return MotivoSemAssinatura;
        if (status == StatusAssinatura.Atrasada)
            return MotivoAtrasada;
        return null;
    }

    /// <summary>Depois de uma recusa do Asaas: tenta de novo amanhã ou chama o operador.</summary>
    public static StatusPremioIndicacao StatusAposRecusa(int tentativas) =>
        tentativas >= MaxTentativas ? StatusPremioIndicacao.Erro : StatusPremioIndicacao.Pendente;
}

/// <summary>Resultado de uma passada do job (para o log).</summary>
public sealed record ResultadoAplicacaoPremios(int Aplicados, int Aguardando, int ComErro);

/// <summary>Aplica no Asaas os prêmios pendentes que já podem valer (job diário e logo após o pagamento).</summary>
public interface IAplicarPremiosIndicacaoUseCase
{
    Task<ResultadoAplicacaoPremios> ExecutarAsync(CancellationToken cancellationToken);
}

/// <summary>Um prêmio visto pelo operador da plataforma.</summary>
public sealed record PremioIndicacaoPlataformaResponse(
    Guid Id,
    Guid TenantId,
    string Conta,
    string ContaIndicada,
    PapelPremioIndicacao Papel,
    StatusPremioIndicacao Status,
    DateTimeOffset CriadoEm,
    DateTimeOffset? AplicadoEm,
    DateOnly? VencimentoDispensado,
    int Tentativas,
    string? Detalhe);

/// <summary>Prêmios mais recentes de todas as contas — só o operador chama.</summary>
public interface IListarPremiosIndicacaoPlataformaUseCase
{
    Task<IReadOnlyList<PremioIndicacaoPlataformaResponse>> ExecutarAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Operador: devolve um prêmio com Erro para Pendente e dispara a aplicação.
/// Antes, conferir no painel do Asaas que a fatura não foi dispensada.
/// </summary>
public interface IReabrirPremioIndicacaoUseCase
{
    Task ExecutarAsync(Guid premioId, Guid? operadorId, CancellationToken cancellationToken);
}
