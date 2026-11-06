using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Cobranca;

/// <summary>Plano oferecido na tela, já com o preço que esta conta pagaria hoje.</summary>
/// <param name="Preco">Preço que vale para esta conta (fundador, enquanto houver vaga).</param>
/// <param name="PrecoNormal">Preço cheio do plano.</param>
/// <param name="NotasPorMes">Nulo = sem limite de notas.</param>
/// <param name="VagasFundadorRestantes">Vagas de fundador que sobram no grupo do plano.</param>
/// <remarks>Para quem já assina, <c>Preco</c> e <c>PrecoDeFundador</c> são os da troca para este plano (RegrasTrocaPlano).</remarks>
public sealed record PlanoOfertaResponse(
    int Codigo,
    string Nome,
    string Descricao,
    int MaxEmpresas,
    int? NotasPorMes,
    decimal Preco,
    decimal PrecoNormal,
    bool PrecoDeFundador,
    int VagasFundadorRestantes);

/// <summary>Tela "Assinatura".</summary>
/// <param name="VagasFundadorRestantes">Vagas de fundador no grupo do perfil da conta (empresas ou escritórios).</param>
public sealed record AssinaturaResponse(
    bool CobrancaAtiva,
    bool Isenta,
    StatusAssinatura Status,
    int? Plano,
    string? PlanoNome,
    decimal? PrecoMensal,
    bool Fundador,
    DateTimeOffset? TesteIniciadoEm,
    DateTimeOffset? TesteTerminaEm,
    int NotasTesteUsadas,
    int NotasTesteLimite,
    bool PodeEmitirEmProducao,
    string? Bloqueio,
    string? Aviso,
    int NotasProducaoNoMes,
    int EmpresasAtivas,
    DateOnly? AtrasadaDesde,
    string? UrlFaturaPendente,
    bool AguardandoPrimeiroPagamento,
    bool PodeAssinar,
    bool PodeCancelar,
    bool PodeTrocarPlano,
    int VagasFundadorRestantes,
    IReadOnlyList<PlanoOfertaResponse> Planos);

public sealed record AssinarRequest(int Plano);

public sealed record AssinarResponse(string? UrlFatura);

public sealed record TrocarPlanoRequest(int Plano);

/// <param name="Descricao">"Mico (R$ 19,00) → Empresa (R$ 39,00)".</param>
public sealed record TrocarPlanoResponse(string PlanoNome, decimal PrecoMensal, bool Fundador, string Descricao);

/// <summary>
/// Troca o plano de quem já assina (sobe ou desce, inclusive Escritórios).
/// O novo valor vale a partir da próxima fatura em aberto ou a gerar.
/// </summary>
public interface ITrocarPlanoUseCase
{
    Task<TrocarPlanoResponse> ExecutarAsync(TrocarPlanoRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Chamado pela emissão (tela, lote e automação) antes de emitir em
/// PRODUÇÃO: lança RegraNegocioException quando a conta não pode emitir
/// (teste acabado, fatura atrasada além da tolerância, cancelada). Também
/// marca o início do teste grátis na primeira nota em produção.
/// </summary>
public interface IVerificadorCobranca
{
    Task GarantirPodeEmitirEmProducaoAsync(Guid tenantId, CancellationToken cancellationToken);
}

public interface IObterAssinaturaUseCase
{
    Task<AssinaturaResponse> ExecutarAsync(CancellationToken cancellationToken);
}

public interface IAssinarUseCase
{
    Task<AssinarResponse> ExecutarAsync(AssinarRequest request, CancellationToken cancellationToken);
}

public interface ICancelarAssinaturaUseCase
{
    Task ExecutarAsync(CancellationToken cancellationToken);
}

/// <summary>Grava e aplica um evento do webhook do Asaas (a autenticação do token é feita antes, no controller).</summary>
public interface IProcessarWebhookAsaasUseCase
{
    Task<string> ExecutarAsync(string payload, CancellationToken cancellationToken);
}

/// <summary>Gateway de pagamento (implementado pelo AsaasClient na Infrastructure).</summary>
public interface IGatewayCobranca
{
    bool Configurado { get; }

    Task<string> CriarClienteAsync(string nome, string cpfCnpj, string? email, string referenciaExterna, CancellationToken cancellationToken);

    Task<string> CriarAssinaturaMensalAsync(string clienteId, decimal valor, DateOnly primeiroVencimento, string descricao, string referenciaExterna, CancellationToken cancellationToken);

    /// <summary>Link da primeira fatura em aberto da assinatura (nulo quando ainda não gerada).</summary>
    Task<string?> ObterUrlFaturaPendenteAsync(string assinaturaId, CancellationToken cancellationToken);

    Task CancelarAssinaturaAsync(string assinaturaId, CancellationToken cancellationToken);

    /// <summary>
    /// Novo valor mensal da assinatura (troca de plano). Atualiza também as
    /// faturas pendentes ainda não pagas, para "a próxima fatura" já vir certa.
    /// </summary>
    Task AlterarValorAssinaturaAsync(string assinaturaId, decimal valor, string descricao, CancellationToken cancellationToken);

    /// <summary>
    /// Fatura em aberto (PENDING, ainda não vencida) mais próxima da assinatura,
    /// ignorando <paramref name="ignorarPagamentoId"/>. Nulo quando o Asaas ainda
    /// não gerou a próxima (ele gera com antecedência configurável na conta).
    /// </summary>
    Task<FaturaGateway?> ObterProximaFaturaEmAbertoAsync(string assinaturaId, string? ignorarPagamentoId, CancellationToken cancellationToken);

    /// <summary>Remove UMA fatura da assinatura (a assinatura continua; pode ser restaurada no painel do Asaas).</summary>
    Task RemoverFaturaAsync(string pagamentoId, CancellationToken cancellationToken);

    /// <summary>Vencimento da próxima fatura que o Asaas ainda vai gerar (nextDueDate da assinatura).</summary>
    Task<DateOnly?> ObterProximoVencimentoAsync(string assinaturaId, CancellationToken cancellationToken);

    /// <summary>Muda o nextDueDate da assinatura (só as faturas futuras).</summary>
    Task AlterarProximoVencimentoAsync(string assinaturaId, DateOnly novoVencimento, CancellationToken cancellationToken);
}

/// <summary>Uma fatura (cobrança) de assinatura no gateway.</summary>
public sealed record FaturaGateway(string Id, DateOnly Vencimento, string? Url);

/// <summary>Faixa no topo do painel (ver RegrasAvisoCobranca.Faixa).</summary>
/// <param name="Nivel">"info", "atencao" ou "bloqueio".</param>
/// <param name="UrlPagar">Link da fatura em aberto; só para quem pode ver a tela Assinatura.</param>
public sealed record FaixaCobrancaResponse(string Nivel, string Mensagem, string? UrlPagar);

/// <summary>Faixa de cobrança da conta logada (nulo = nada a mostrar, inclusive com a cobrança desligada).</summary>
public interface IObterFaixaCobrancaUseCase
{
    Task<FaixaCobrancaResponse?> ExecutarAsync(bool incluirLinkPagamento, CancellationToken cancellationToken);
}

/// <summary>Resultado de uma passada dos avisos de cobrança numa conta.</summary>
/// <param name="Enviados">Avisos que saíram agora (os já avisados antes não entram).</param>
/// <param name="Motivo">Por que nada saiu, quando nada saiu.</param>
public sealed record AvisosCobrancaResult(IReadOnlyList<TipoAvisoCobranca> Enviados, IReadOnlyList<string> Destinatarios, string? Motivo);

/// <summary>
/// Manda os avisos de cobrança devidos a uma conta (RegrasAvisoCobranca),
/// um e-mail por administrador com e-mail confirmado, sem repetir aviso.
/// O Tenant da conta precisa estar definido no escopo (o EmailLog é por conta).
/// </summary>
public interface IEnviarAvisosCobrancaUseCase
{
    Task<AvisosCobrancaResult> ExecutarAsync(Guid tenantId, CancellationToken cancellationToken);
}
