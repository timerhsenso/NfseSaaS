using Hangfire;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.UseCases.Cobranca;

namespace NfseSaaS.Infrastructure.Jobs;

/// <summary>
/// Job diário do reajuste de assinantes (B2): aplica no Asaas os reajustes
/// que chegaram na data. Atributos do Hangfire na interface (ver
/// ImportarIndicesEconomicosJob). Sem retry do Hangfire: alterar o valor é
/// idempotente e a próxima passada repete o que falhou (até
/// RegrasReajusteAssinatura.MaxTentativas).
/// </summary>
public interface IAplicarReajustesAssinaturaJob
{
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 0)]
    Task ExecutarAsync();
}

public sealed class AplicarReajustesAssinaturaJob : IAplicarReajustesAssinaturaJob
{
    public const string JobId = "aplicar-reajustes-assinatura";

    /// <summary>07:20 de Brasília (antes da conciliação das 07:50).</summary>
    public const string Cron = "20 7 * * *";

    private readonly IAplicarReajustesAssinaturaUseCase _aplicar;
    private readonly ILogger<AplicarReajustesAssinaturaJob> _logger;

    public AplicarReajustesAssinaturaJob(IAplicarReajustesAssinaturaUseCase aplicar, ILogger<AplicarReajustesAssinaturaJob> logger)
    {
        _aplicar = aplicar;
        _logger = logger;
    }

    public async Task ExecutarAsync()
    {
        var resultado = await _aplicar.ExecutarAsync(CancellationToken.None);

        if (resultado.Aplicados > 0 || resultado.Cancelados > 0 || resultado.Falhas > 0)
            _logger.LogInformation("Reajustes de assinatura: {Aplicados} aplicado(s), {Cancelados} cancelado(s), {Falhas} com falha.",
                resultado.Aplicados, resultado.Cancelados, resultado.Falhas);

        // Dispara o e-mail de falha de job (AlertaFalhaJobFilter) para o operador olhar.
        if (resultado.Falhas > 0)
            throw new InvalidOperationException($"{resultado.Falhas} reajuste(s) de assinatura com falha no Asaas — ver Plataforma › Planos › Reajustes.");
    }
}
