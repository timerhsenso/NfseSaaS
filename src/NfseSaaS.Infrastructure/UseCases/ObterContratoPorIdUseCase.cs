using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ObterContratoPorIdUseCase : IObterContratoPorIdUseCase
{
    private readonly AppDbContext _db;

    public ObterContratoPorIdUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ContratoResponse> ExecutarAsync(Guid id, CancellationToken cancellationToken)
    {
        var resultado = await (
            from c in _db.Contratos.AsNoTracking()
            where c.Id == id
            join cli in _db.Clientes.AsNoTracking() on c.ClienteId equals cli.Id
            join emp in _db.Empresas.AsNoTracking() on c.EmpresaId equals emp.Id
            select new { Contrato = c, ClienteNome = cli.Nome, emp.DiasAlertaReajusteContratoPadrao })
            .FirstOrDefaultAsync(cancellationToken);

        if (resultado is null)
            throw new RecursoNaoEncontradoException($"Contrato {id} não encontrado.");

        var servicos = await (
            from cs in _db.ContratoServicos.AsNoTracking()
            where cs.ContratoId == id
            join srv in _db.Servicos.AsNoTracking() on cs.ServicoId equals srv.Id
            select new ContratoServicoResponse(cs.Id, cs.ServicoId, srv.Descricao, cs.Quantidade, cs.ValorUnitario, cs.ValorTotal))
            .ToListAsync(cancellationToken);

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var dataProximoReajuste = SituacaoContratoCalculator.CalcularDataProximoReajuste(resultado.Contrato);
        var diasAlertaEfetivo = SituacaoContratoCalculator.DiasAlertaEfetivo(resultado.Contrato, resultado.DiasAlertaReajusteContratoPadrao);
        var situacao = SituacaoContratoCalculator.CalcularSituacao(resultado.Contrato, resultado.DiasAlertaReajusteContratoPadrao, hoje);

        return new ContratoResponse(
            resultado.Contrato.Id, resultado.Contrato.EmpresaId, resultado.Contrato.ClienteId, resultado.ClienteNome,
            resultado.Contrato.Descricao, servicos, resultado.Contrato.ValorAtual,
            resultado.Contrato.DataInicioContrato, resultado.Contrato.PeriodicidadeReajusteMeses, resultado.Contrato.IndiceReajuste,
            resultado.Contrato.DataUltimoReajuste, resultado.Contrato.DiasAlertaOverride, diasAlertaEfetivo, resultado.Contrato.Observacao,
            dataProximoReajuste, situacao, resultado.Contrato.Status, resultado.Contrato.DataFim, resultado.Contrato.TipoCobranca,
            resultado.Contrato.PermitirAlterarValorNaEmissao, resultado.Contrato.Ativo, resultado.Contrato.CreatedAt, resultado.Contrato.UpdatedAt);
    }
}
