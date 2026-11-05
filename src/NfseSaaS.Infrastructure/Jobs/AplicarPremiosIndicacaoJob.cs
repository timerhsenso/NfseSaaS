using Hangfire;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.UseCases.Indicacao;

namespace NfseSaaS.Infrastructure.Jobs;

/// <summary>
/// Job do prêmio do "Indique e ganhe" — atributos do Hangfire na interface
/// (ver ImportarIndicesEconomicosJob). Uma execução por vez: o webhook
/// dispara uma logo após o pagamento e a varredura diária pega o que ficou
/// guardado (ex.: quem indicou e passou a pagar depois). Sem retry
/// automático do Hangfire: as tentativas são controladas por prêmio.
/// </summary>
public interface IAplicarPremiosIndicacaoJob
{
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 0)]
    Task ExecutarAsync();
}

public sealed class AplicarPremiosIndicacaoJob : IAplicarPremiosIndicacaoJob
{
    public const string JobId = "aplicar-premios-indicacao";

    /// <summary>07:40 de Brasília.</summary>
    public const string Cron = "40 7 * * *";

    private readonly IAplicarPremiosIndicacaoUseCase _aplicar;
    private readonly ILogger<AplicarPremiosIndicacaoJob> _logger;

    public AplicarPremiosIndicacaoJob(IAplicarPremiosIndicacaoUseCase aplicar, ILogger<AplicarPremiosIndicacaoJob> logger)
    {
        _aplicar = aplicar;
        _logger = logger;
    }

    public async Task ExecutarAsync()
    {
        var resultado = await _aplicar.ExecutarAsync(CancellationToken.None);

        if (resultado.Aplicados > 0 || resultado.ComErro > 0)
            _logger.LogInformation("Prêmios de indicação: {Aplicados} aplicado(s), {Aguardando} guardado(s), {ComErro} com erro.",
                resultado.Aplicados, resultado.Aguardando, resultado.ComErro);

        // Dispara o e-mail de falha de job (AlertaFalhaJobFilter) para o operador olhar.
        if (resultado.ComErro > 0)
            throw new InvalidOperationException($"{resultado.ComErro} prêmio(s) de indicação com erro — ver Plataforma › Contas.");
    }
}
