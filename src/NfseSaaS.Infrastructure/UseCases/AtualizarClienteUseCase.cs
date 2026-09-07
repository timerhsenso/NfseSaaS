using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class AtualizarClienteUseCase : IAtualizarClienteUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public AtualizarClienteUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task ExecutarAsync(Guid id, AtualizarClienteRequest request, CancellationToken cancellationToken)
    {
        var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (cliente is null)
            throw new RecursoNaoEncontradoException($"Cliente {id} não encontrado.");

        cliente.Nome = request.Nome;
        cliente.Email = request.Email;
        cliente.Telefone = request.Telefone;
        cliente.CodigoMunicipio = request.CodigoMunicipio;
        cliente.Cep = request.Cep;
        cliente.Logradouro = request.Logradouro;
        cliente.Numero = request.Numero;
        cliente.Bairro = request.Bairro;

        _auditLogWriter.Registrar("AtualizarCliente", "Cliente", cliente.Id, new { cliente.Nome });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
