using NfseSaaS.Application.Abstractions;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Email;

public sealed class EmailQueueService : IEmailQueueService
{
    private readonly AppDbContext _db;
    private readonly IBackgroundEmailQueue _fila;

    public EmailQueueService(AppDbContext db, IBackgroundEmailQueue fila)
    {
        _db = db;
        _fila = fila;
    }

    public async Task<Guid> EnfileirarAsync(string tipo, Guid? usuarioId, string destinatario, string assunto, string corpoHtml, CancellationToken cancellationToken)
    {
        var log = new EmailLog
        {
            Tipo = tipo,
            UsuarioId = usuarioId,
            Destinatario = destinatario,
            Assunto = assunto,
            CorpoHtml = corpoHtml
        };

        _db.EmailLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);

        _fila.Enfileirar(new ItemFilaEmail(log.Id, destinatario, assunto, corpoHtml));

        return log.Id;
    }
}
