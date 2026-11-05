using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Cobranca;

/// <summary>Plano oferecido na tela, já com o preço que esta conta pagaria hoje.</summary>
/// <param name="Preco">Preço que vale para esta conta (fundador, enquanto houver vaga).</param>
/// <param name="PrecoNormal">Preço cheio do plano.</param>
public sealed record PlanoOfertaResponse(
    PlanoCodigo Codigo,
    string Nome,
    string Descricao,
    int MaxEmpresas,
    int NotasPorMes,
    decimal Preco,
    decimal PrecoNormal,
    bool PrecoDeFundador);

/// <summary>Tela "Assinatura".</summary>
public sealed record AssinaturaResponse(
    bool CobrancaAtiva,
    bool Isenta,
    StatusAssinatura Status,
    PlanoCodigo? Plano,
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
    int VagasFundadorRestantes,
    IReadOnlyList<PlanoOfertaResponse> Planos);

public sealed record AssinarRequest(PlanoCodigo Plano);

public sealed record AssinarResponse(string? UrlFatura);

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
}
