using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Email;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>
/// A fila de e-mail é em memória (ver BackgroundEmailQueue) — se a
/// aplicação reiniciar (deploy, IIS reciclando) com item ainda não
/// processado, a fila em si se perde. O que NÃO se perde é o EmailLog
/// (gravado como Pendente antes de entrar na fila) — este seeder só
/// reenfileira, no boot, tudo que ficou Pendente.
/// </summary>
public static class EmailPendenteRecuperador
{
    public static async Task ReenfileirarPendentesAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fila = scope.ServiceProvider.GetRequiredService<IBackgroundEmailQueue>();

        var pendentes = await db.EmailLogs.IgnoreQueryFilters()
            .Where(e => e.Status == StatusEmailLog.Pendente)
            .ToListAsync();

        foreach (var log in pendentes)
            fila.Enfileirar(new ItemFilaEmail(log.Id, log.Destinatario, log.Assunto, log.CorpoHtml));
    }
}
