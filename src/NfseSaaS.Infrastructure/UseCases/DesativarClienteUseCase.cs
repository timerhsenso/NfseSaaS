using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>Soft delete — Nfse referencia ClienteId, exclusão física quebraria histórico fiscal.</summary>
public sealed class DesativarClienteUseCase : IDesativarClienteUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public DesativarClienteUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task ExecutarAsync(Guid id, CancellationToken cancellationToken)
    {
        var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (cliente is null)
            throw new RecursoNaoEncontradoException($"Cliente {id} não encontrado.");

        if (!cliente.Ativo)
            return;

        cliente.Ativo = false;

        _auditLogWriter.Registrar("DesativarCliente", "Cliente", cliente.Id);

        await _db.SaveChangesAsync(cancellationToken);
    }
}
