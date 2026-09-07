using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Servicos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class DesativarServicoUseCase : IDesativarServicoUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public DesativarServicoUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task ExecutarAsync(Guid id, CancellationToken cancellationToken)
    {
        var servico = await _db.Servicos.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (servico is null)
            throw new RecursoNaoEncontradoException($"Serviço {id} não encontrado.");

        if (!servico.Ativo)
            return;

        servico.Ativo = false;

        _auditLogWriter.Registrar("DesativarServico", "Servico", servico.Id);

        await _db.SaveChangesAsync(cancellationToken);
    }
}
