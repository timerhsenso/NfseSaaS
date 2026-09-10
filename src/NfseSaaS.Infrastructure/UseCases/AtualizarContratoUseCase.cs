using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class AtualizarContratoUseCase : IAtualizarContratoUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public AtualizarContratoUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task ExecutarAsync(Guid id, AtualizarContratoRequest request, CancellationToken cancellationToken)
    {
        var contrato = await _db.Contratos.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Contrato {id} não encontrado.");

        var servicoExiste = await _db.Servicos.AnyAsync(s => s.Id == request.ServicoId, cancellationToken);
        if (!servicoExiste)
            throw new RecursoNaoEncontradoException($"Serviço {request.ServicoId} não encontrado.");

        contrato.ServicoId = request.ServicoId;
        contrato.Descricao = request.Descricao;
        contrato.DataInicioContrato = request.DataInicioContrato;
        contrato.PeriodicidadeReajusteMeses = request.PeriodicidadeReajusteMeses;
        contrato.IndiceReajuste = request.IndiceReajuste;
        contrato.DiasAlertaOverride = request.DiasAlertaOverride;

        _auditLogWriter.Registrar("AtualizarContrato", "Contrato", contrato.Id, new { contrato.Descricao });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
