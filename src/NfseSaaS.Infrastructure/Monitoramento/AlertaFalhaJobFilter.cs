using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;

namespace NfseSaaS.Infrastructure.Monitoramento;

/// <summary>
/// Filtro global do Hangfire: quando um job vai para "Failed" — ou seja,
/// falhou e o [AutomaticRetry] já desistiu (as tentativas intermediárias vão
/// para "Scheduled", não passam por aqui) — registra um Error no log. O sink
/// de alertas transforma esse Error em e-mail para o operador.
///
/// Os erros do próprio Hangfire (categoria "Hangfire.*") ficam fora do
/// alerta (ver AlertaErroSink): um único e-mail claro, daqui, por job.
/// </summary>
public sealed class AlertaFalhaJobFilter : IApplyStateFilter
{
    private readonly ILogger _logger;

    public AlertaFalhaJobFilter(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<AlertaFalhaJobFilter>();
    }

    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        if (context.NewState is not FailedState falha)
            return;

        var job = context.BackgroundJob.Job;
        var nome = job is null ? "(desconhecido)" : $"{job.Type.Name}.{job.Method.Name}";

        _logger.LogError(falha.Exception,
            "Job {Job} falhou de vez (id {JobId}) depois das novas tentativas: {Motivo}",
            nome, context.BackgroundJob.Id, falha.Exception?.Message);
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
    }
}
