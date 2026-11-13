using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Infrastructure.Jobs;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>DOC1: registra (ou atualiza) no Hangfire o job diário do aviso de documentos vencendo. Roda na subida (Program.cs).</summary>
public static class AvisoDocumentosEmpresaJobSincronizador
{
    public static Task SincronizarAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

        recurringJobManager.AddOrUpdate<IAvisarDocumentosEmpresaVencendoJob>(
            AvisarDocumentosEmpresaVencendoJob.JobId,
            job => job.ExecutarAsync(),
            AvisarDocumentosEmpresaVencendoJob.Cron,
            new RecurringJobOptions { TimeZone = AutomacaoNotaMensalJobHelper.FusoBrasilia });

        return Task.CompletedTask;
    }
}
