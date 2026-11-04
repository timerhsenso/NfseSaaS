using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Infrastructure.Jobs;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>
/// Registra (ou atualiza) no Hangfire o job diário do aviso de certificado
/// vencendo — ver AvisarCertificadosVencendoJob. Roda na subida (Program.cs).
/// </summary>
public static class AvisoCertificadoJobSincronizador
{
    public static Task SincronizarAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

        recurringJobManager.AddOrUpdate<IAvisarCertificadosVencendoJob>(
            AvisarCertificadosVencendoJob.JobId,
            job => job.ExecutarAsync(),
            AvisarCertificadosVencendoJob.Cron,
            new RecurringJobOptions { TimeZone = AutomacaoNotaMensalJobHelper.FusoBrasilia });

        return Task.CompletedTask;
    }
}
