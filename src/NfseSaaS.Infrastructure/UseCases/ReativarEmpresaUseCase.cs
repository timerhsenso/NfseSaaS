using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Empresas;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ReativarEmpresaUseCase : IReativarEmpresaUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public ReativarEmpresaUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task ExecutarAsync(Guid id, CancellationToken cancellationToken)
    {
        var empresa = await _db.Empresas.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Empresa {id} não encontrada.");

        if (empresa.Ativo)
            return;

        empresa.Ativo = true;

        _auditLogWriter.Registrar("ReativarEmpresa", "Empresa", empresa.Id);

        await _db.SaveChangesAsync(cancellationToken);
    }
}
