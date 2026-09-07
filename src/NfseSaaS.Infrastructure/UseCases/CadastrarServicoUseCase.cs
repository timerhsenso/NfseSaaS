using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Servicos;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class CadastrarServicoUseCase : ICadastrarServicoUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public CadastrarServicoUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<Guid> ExecutarAsync(CadastrarServicoRequest request, CancellationToken cancellationToken)
    {
        var empresaExiste = await _db.Empresas.AnyAsync(e => e.Id == request.EmpresaId, cancellationToken);
        if (!empresaExiste)
            throw new RecursoNaoEncontradoException($"Empresa {request.EmpresaId} não encontrada.");

        var servico = new Servico
        {
            EmpresaId = request.EmpresaId,
            Descricao = request.Descricao,
            CodigoTributacaoNacional = request.CodigoTributacaoNacional,
            CodigoNbs = request.CodigoNbs,
            ValorPadrao = request.ValorPadrao
        };

        _db.Servicos.Add(servico);

        _auditLogWriter.Registrar("CadastrarServico", "Servico", servico.Id, new { servico.EmpresaId, servico.Descricao });

        await _db.SaveChangesAsync(cancellationToken);

        return servico.Id;
    }
}
