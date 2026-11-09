using Hangfire;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.UseCases.DadosConta;

namespace NfseSaaS.Infrastructure.Jobs;

/// <summary>
/// Expurgo diário das contas encerradas há 90 dias (D2) — atributos do
/// Hangfire na interface (ver ImportarIndicesEconomicosJob). Sem retry
/// automático: uma conta que falhar fica para o dia seguinte, e a falha
/// dispara o alerta de job para o operador olhar.
/// </summary>
public interface IExpurgarContasEncerradasJob
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 0)]
    Task ExecutarAsync();
}

public sealed class ExpurgarContasEncerradasJob : IExpurgarContasEncerradasJob
{
    public const string JobId = "expurgar-contas-encerradas";

    /// <summary>05:10 de Brasília (depois do backup das 03:30: o último backup ainda tem a conta).</summary>
    public const string Cron = "10 5 * * *";

    private readonly IExpurgarContasEncerradasUseCase _expurgar;
    private readonly ILogger<ExpurgarContasEncerradasJob> _logger;

    public ExpurgarContasEncerradasJob(IExpurgarContasEncerradasUseCase expurgar, ILogger<ExpurgarContasEncerradasJob> logger)
    {
        _expurgar = expurgar;
        _logger = logger;
    }

    public async Task ExecutarAsync()
    {
        var resultado = await _expurgar.ExecutarAsync(DataBrasilia.Hoje(), CancellationToken.None);
        _logger.LogDebug("Expurgo: {Excluidas} excluída(s), {Anonimizadas} anonimizada(s), {ComErro} com erro.",
            resultado.Excluidas, resultado.Anonimizadas, resultado.ComErro);

        if (resultado.ComErro > 0)
            throw new InvalidOperationException($"{resultado.ComErro} conta(s) encerrada(s) não puderam ter os dados apagados — ver o log.");
    }
}
