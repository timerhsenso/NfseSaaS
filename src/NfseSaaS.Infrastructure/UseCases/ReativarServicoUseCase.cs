using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Servicos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ReativarServicoUseCase : IReativarServicoUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public ReativarServicoUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task ExecutarAsync(Guid id, CancellationToken cancellationToken)
    {
        var servico = await _db.Servicos.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Serviço {id} não encontrado.");

        if (servico.Ativo)
            return;

        servico.Ativo = true;

        _auditLogWriter.Registrar("ReativarServico", "Servico", servico.Id);

        await _db.SaveChangesAsync(cancellationToken);
    }
}
