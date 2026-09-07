using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.AuditLogs;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ObterAuditLogPorIdUseCase : IObterAuditLogPorIdUseCase
{
    private readonly AppDbContext _db;

    public ObterAuditLogPorIdUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<AuditLogResponse> ExecutarAsync(Guid id, CancellationToken cancellationToken)
    {
        var auditLog = await _db.AuditLogs.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (auditLog is null)
            throw new RecursoNaoEncontradoException($"AuditLog {id} não encontrado.");

        return new AuditLogResponse(
            auditLog.Id, auditLog.UserId, auditLog.DataHora, auditLog.Operacao, auditLog.Entidade,
            auditLog.EntidadeId, auditLog.IpAddress, auditLog.Dados);
    }
}
