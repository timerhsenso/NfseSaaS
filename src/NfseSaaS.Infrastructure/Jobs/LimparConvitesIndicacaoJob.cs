using Hangfire;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.UseCases.Indicacao;

namespace NfseSaaS.Infrastructure.Jobs;

/// <summary>
/// Retenção diária dos convites do "Indique e ganhe" — atributos do Hangfire
/// na interface (ver ImportarIndicesEconomicosJob). Idempotente: se falhar,
/// a próxima passada apaga o que ficou.
/// </summary>
public interface ILimparConvitesIndicacaoJob
{
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 2)]
    Task ExecutarAsync();
}

public sealed class LimparConvitesIndicacaoJob : ILimparConvitesIndicacaoJob
{
    public const string JobId = "limpar-convites-indicacao";

    /// <summary>04:20 de Brasília (depois do backup das 03:30, longe dos jobs de cobrança).</summary>
    public const string Cron = "20 4 * * *";

    private readonly ILimparConvitesIndicacaoUseCase _limpar;
    private readonly ILogger<LimparConvitesIndicacaoJob> _logger;

    public LimparConvitesIndicacaoJob(ILimparConvitesIndicacaoUseCase limpar, ILogger<LimparConvitesIndicacaoJob> logger)
    {
        _limpar = limpar;
        _logger = logger;
    }

    public async Task ExecutarAsync()
    {
        var resultado = await _limpar.ExecutarAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        _logger.LogDebug("Limpeza dos convites de indicação: {Convites} convite(s), {Emails} e-mail(s).",
            resultado.Convites, resultado.EmailsEnviados);
    }
}
