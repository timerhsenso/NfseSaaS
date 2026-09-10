using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class CadastrarContratoUseCase : ICadastrarContratoUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public CadastrarContratoUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<Guid> ExecutarAsync(CadastrarContratoRequest request, CancellationToken cancellationToken)
    {
        var empresaExiste = await _db.Empresas.AnyAsync(e => e.Id == request.EmpresaId, cancellationToken);
        if (!empresaExiste)
            throw new RecursoNaoEncontradoException($"Empresa {request.EmpresaId} não encontrada.");

        var clienteExiste = await _db.Clientes.AnyAsync(c => c.Id == request.ClienteId, cancellationToken);
        if (!clienteExiste)
            throw new RecursoNaoEncontradoException($"Cliente {request.ClienteId} não encontrado.");

        var servicoExiste = await _db.Servicos.AnyAsync(s => s.Id == request.ServicoId, cancellationToken);
        if (!servicoExiste)
            throw new RecursoNaoEncontradoException($"Serviço {request.ServicoId} não encontrado.");

        var contrato = new Contrato
        {
            EmpresaId = request.EmpresaId,
            ClienteId = request.ClienteId,
            ServicoId = request.ServicoId,
            Descricao = request.Descricao,
            ValorAtual = request.ValorAtual,
            DataInicioContrato = request.DataInicioContrato,
            PeriodicidadeReajusteMeses = request.PeriodicidadeReajusteMeses,
            IndiceReajuste = request.IndiceReajuste,
            DiasAlertaOverride = request.DiasAlertaOverride
        };

        _db.Contratos.Add(contrato);

        _auditLogWriter.Registrar("CadastrarContrato", "Contrato", contrato.Id, new { contrato.EmpresaId, contrato.ClienteId, contrato.Descricao, contrato.ValorAtual });

        await _db.SaveChangesAsync(cancellationToken);

        return contrato.Id;
    }
}
