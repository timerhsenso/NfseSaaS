using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ReativarClienteUseCase : IReativarClienteUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public ReativarClienteUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task ExecutarAsync(Guid id, CancellationToken cancellationToken)
    {
        var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Cliente {id} não encontrado.");

        if (cliente.Ativo)
            return;

        cliente.Ativo = true;

        _auditLogWriter.Registrar("ReativarCliente", "Cliente", cliente.Id);

        await _db.SaveChangesAsync(cancellationToken);
    }
}
