using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Infrastructure.Jobs;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>Registra (ou atualiza) no Hangfire o expurgo diário das contas encerradas (D2). Roda na subida (Program.cs).</summary>
public static class ExpurgoContasJobSincronizador
{
    public static Task SincronizarAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

        recurringJobManager.AddOrUpdate<IExpurgarContasEncerradasJob>(
            ExpurgarContasEncerradasJob.JobId,
            job => job.ExecutarAsync(),
            ExpurgarContasEncerradasJob.Cron,
            new RecurringJobOptions { TimeZone = AutomacaoNotaMensalJobHelper.FusoBrasilia });

        return Task.CompletedTask;
    }
}
