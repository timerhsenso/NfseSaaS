using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NfseSaaS.Infrastructure.IndicesEconomicos;
using NfseSaaS.Infrastructure.Jobs;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>
/// No startup: agenda (ou remove, se IndicesEconomicos:ImportacaoAutomatica
/// = false) o job diário de importação; e, se a tabela está VAZIA (primeiro
/// deploy), enfileira uma importação imediata — sem isso a tela de Índices
/// ficaria vazia até as 09:40 do dia seguinte. Mesmo cuidado do
/// AutomacaoNotaMensalJobSincronizador: Hangfire via DI, nunca a fachada
/// estática (JobStorage.Current ainda não existe antes de app.Run()).
/// </summary>
public static class IndicesEconomicosJobSincronizador
{
    public static async Task SincronizarAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<IndicesEconomicosOptions>>().Value;
        var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

        if (!options.ImportacaoAutomatica)
        {
            recurringJobManager.RemoveIfExists(ImportarIndicesEconomicosJob.JobId);
            return;
        }

        recurringJobManager.AddOrUpdate<IImportarIndicesEconomicosJob>(
            ImportarIndicesEconomicosJob.JobId,
            job => job.ExecutarAsync(),
            ImportarIndicesEconomicosJob.Cron,
            new RecurringJobOptions { TimeZone = AutomacaoNotaMensalJobHelper.FusoBrasilia });

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await db.IndicesEconomicosMensais.AnyAsync())
        {
            var backgroundJobClient = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
            backgroundJobClient.Enqueue<IImportarIndicesEconomicosJob>(job => job.ExecutarAsync());
        }
    }
}
