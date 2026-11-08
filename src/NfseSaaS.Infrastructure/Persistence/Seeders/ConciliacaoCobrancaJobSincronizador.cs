using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Infrastructure.Jobs;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>Registra (ou atualiza) no Hangfire a conciliação diária com o Asaas. Roda na subida (Program.cs).</summary>
public static class ConciliacaoCobrancaJobSincronizador
{
    public static Task SincronizarAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

        recurringJobManager.AddOrUpdate<IConciliarCobrancaJob>(
            ConciliarCobrancaJob.JobId,
            job => job.ExecutarAsync(),
            ConciliarCobrancaJob.Cron,
            new RecurringJobOptions { TimeZone = AutomacaoNotaMensalJobHelper.FusoBrasilia });

        return Task.CompletedTask;
    }
}
