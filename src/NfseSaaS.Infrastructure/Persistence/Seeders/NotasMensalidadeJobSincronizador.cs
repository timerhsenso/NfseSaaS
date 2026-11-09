using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Infrastructure.Jobs;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>Registra (ou atualiza) no Hangfire a passada diária da NFS-e da mensalidade (N1). Roda na subida (Program.cs).</summary>
public static class NotasMensalidadeJobSincronizador
{
    public static Task SincronizarAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

        recurringJobManager.AddOrUpdate<IEmitirNotasMensalidadeJob>(
            EmitirNotasMensalidadeJob.JobId,
            job => job.ExecutarAsync(),
            EmitirNotasMensalidadeJob.Cron,
            new RecurringJobOptions { TimeZone = AutomacaoNotaMensalJobHelper.FusoBrasilia });

        return Task.CompletedTask;
    }
}
