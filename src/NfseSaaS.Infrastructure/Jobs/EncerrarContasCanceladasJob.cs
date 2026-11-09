using Hangfire;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.UseCases.DadosConta;

namespace NfseSaaS.Infrastructure.Jobs;

/// <summary>
/// Encerramento automático das contas com assinatura cancelada (D2, parte 2)
/// — atributos do Hangfire na interface (ver ImportarIndicesEconomicosJob).
/// Roda todo dia, mas só faz algo com Encerramento:AutomaticoAposCancelarDias
/// preenchido (desligado por padrão).
/// </summary>
public interface IEncerrarContasCanceladasJob
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 0)]
    Task ExecutarAsync();
}

public sealed class EncerrarContasCanceladasJob : IEncerrarContasCanceladasJob
{
    public const string JobId = "encerrar-contas-canceladas";

    /// <summary>05:00 de Brasília (antes do expurgo das 05:10).</summary>
    public const string Cron = "0 5 * * *";

    private readonly IEncerrarContasCanceladasUseCase _encerrar;
    private readonly ILogger<EncerrarContasCanceladasJob> _logger;

    public EncerrarContasCanceladasJob(IEncerrarContasCanceladasUseCase encerrar, ILogger<EncerrarContasCanceladasJob> logger)
    {
        _encerrar = encerrar;
        _logger = logger;
    }

    public async Task ExecutarAsync()
    {
        var r = await _encerrar.ExecutarAsync(DataBrasilia.Hoje(), CancellationToken.None);
        if (r.Desligado)
        {
            _logger.LogDebug("Encerramento automático desligado (Encerramento:AutomaticoAposCancelarDias vazio).");
            return;
        }
        if (r.ComErro > 0)
            throw new InvalidOperationException($"{r.ComErro} conta(s) não puderam ser avisadas ou encerradas — ver o log.");
    }
}
