using NfseSaaS.Application.Common;
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
            .AsNoTracking()
            .ToListAsync();

        // Corpo com token escondido (convite, senha, confirmação): o link
        // verdadeiro se perdeu com o reinício; mandar o corpo gravado
        // entregaria um link quebrado. Vira Falhou com a orientação.
        // ExecuteUpdate: roda sem conta definida (boot), para todas as contas.
        var comSegredo = pendentes.Where(l => MascaramentoEmail.TemSegredoOculto(l.CorpoHtml)).Select(l => l.Id).ToList();
        if (comSegredo.Count > 0)
        {
            var erro = "Aplicação reiniciada antes do envio. " + MascaramentoEmail.GereNovoLink;
            await db.EmailLogs.IgnoreQueryFilters()
                .Where(e => comSegredo.Contains(e.Id))
                .ExecuteUpdateAsync(u => u
                    .SetProperty(e => e.Status, StatusEmailLog.Falhou)
                    .SetProperty(e => e.ErroDetalhe, erro));
        }

        foreach (var log in pendentes.Where(l => !comSegredo.Contains(l.Id)))
            fila.Enfileirar(new ItemFilaEmail(log.Id, log.Destinatario, log.Assunto, log.CorpoHtml));
    }
}
