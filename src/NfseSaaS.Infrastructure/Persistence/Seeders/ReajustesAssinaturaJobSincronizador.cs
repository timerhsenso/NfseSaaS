using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Infrastructure.Jobs;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>Registra (ou atualiza) no Hangfire a aplicação diária dos reajustes de assinatura. Roda na subida (Program.cs).</summary>
public static class ReajustesAssinaturaJobSincronizador
{
    public static Task SincronizarAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

        recurringJobManager.AddOrUpdate<IAplicarReajustesAssinaturaJob>(
            AplicarReajustesAssinaturaJob.JobId,
            job => job.ExecutarAsync(),
            AplicarReajustesAssinaturaJob.Cron,
            new RecurringJobOptions { TimeZone = AutomacaoNotaMensalJobHelper.FusoBrasilia });

        return Task.CompletedTask;
    }
}
