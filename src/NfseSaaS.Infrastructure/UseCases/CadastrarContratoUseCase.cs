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

        var servicoIds = request.Servicos.Select(s => s.ServicoId).Distinct().ToList();
        var totalServicosEncontrados = await _db.Servicos.CountAsync(s => servicoIds.Contains(s.Id), cancellationToken);
        if (totalServicosEncontrados != servicoIds.Count)
            throw new RecursoNaoEncontradoException("Um ou mais Serviços informados não foram encontrados.");

        var valorAtual = request.Servicos.Sum(s => s.Quantidade * s.ValorUnitario);

        var contrato = new Contrato
        {
            EmpresaId = request.EmpresaId,
            ClienteId = request.ClienteId,
            Descricao = request.Descricao,
            ValorAtual = valorAtual,
            DataInicioContrato = request.DataInicioContrato,
            PeriodicidadeReajusteMeses = request.PeriodicidadeReajusteMeses,
            IndiceReajuste = request.IndiceReajuste,
            DiasAlertaOverride = request.DiasAlertaOverride,
            Observacao = request.Observacao,
            DataFim = request.DataFim,
            TipoCobranca = request.TipoCobranca,
            PermitirAlterarValorNaEmissao = request.PermitirAlterarValorNaEmissao
        };

        _db.Contratos.Add(contrato);

        foreach (var servico in request.Servicos)
        {
            _db.ContratoServicos.Add(new ContratoServico
            {
                ContratoId = contrato.Id,
                ServicoId = servico.ServicoId,
                Quantidade = servico.Quantidade,
                ValorUnitario = servico.ValorUnitario
            });
        }

        _auditLogWriter.Registrar("CadastrarContrato", "Contrato", contrato.Id, new { contrato.EmpresaId, contrato.ClienteId, contrato.Descricao, contrato.ValorAtual, TotalLinhas = request.Servicos.Count });

        await _db.SaveChangesAsync(cancellationToken);

        return contrato.Id;
    }
}
