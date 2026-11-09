using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Infrastructure.Jobs;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>Registra (ou atualiza) no Hangfire a limpeza diária das exportações dos dados (D1). Roda na subida (Program.cs).</summary>
public static class ExportacaoDadosJobSincronizador
{
    public static Task SincronizarAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

        recurringJobManager.AddOrUpdate<IExportacaoDadosJob>(
            ExportacaoDadosJob.JobIdLimpeza,
            job => job.LimparAsync(),
            ExportacaoDadosJob.CronLimpeza,
            new RecurringJobOptions { TimeZone = AutomacaoNotaMensalJobHelper.FusoBrasilia });

        return Task.CompletedTask;
    }
}
