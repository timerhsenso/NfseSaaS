using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Servicos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class AtualizarServicoUseCase : IAtualizarServicoUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public AtualizarServicoUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task ExecutarAsync(Guid id, AtualizarServicoRequest request, CancellationToken cancellationToken)
    {
        var servico = await _db.Servicos.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (servico is null)
            throw new RecursoNaoEncontradoException($"Serviço {id} não encontrado.");

        servico.Descricao = request.Descricao;
        servico.CodigoTributacaoNacional = request.CodigoTributacaoNacional;
        servico.CodigoNbs = request.CodigoNbs;
        servico.ValorPadrao = request.ValorPadrao;

        _auditLogWriter.Registrar("AtualizarServico", "Servico", servico.Id, new { servico.Descricao });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
