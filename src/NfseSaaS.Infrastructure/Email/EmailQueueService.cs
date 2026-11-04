using Microsoft.EntityFrameworkCore;
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

    public Task<Guid> EnfileirarAsync(string tipo, Guid? usuarioId, string destinatario, string assunto, string corpoHtml, CancellationToken cancellationToken) =>
        EnfileirarAsync(new EmailParaEnfileirar(tipo, usuarioId, destinatario, assunto, corpoHtml), cancellationToken);

    public async Task<Guid> EnfileirarAsync(EmailParaEnfileirar email, CancellationToken cancellationToken)
    {
        var log = new EmailLog
        {
            Tipo = email.Tipo,
            UsuarioId = email.UsuarioId,
            Destinatario = email.Destinatario,
            Assunto = email.Assunto,
            CorpoHtml = email.CorpoHtml,
            NfseId = email.NfseId,
            ChaveDeduplicacao = email.ChaveDeduplicacao
        };

        _db.EmailLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);

        _fila.Enfileirar(new ItemFilaEmail(log.Id, email.Destinatario, email.Assunto, email.CorpoHtml));

        return log.Id;
    }

    public Task<bool> JaEnfileiradoAsync(string chaveDeduplicacao, CancellationToken cancellationToken) =>
        _db.EmailLogs.AsNoTracking().AnyAsync(e => e.ChaveDeduplicacao == chaveDeduplicacao, cancellationToken);
}
