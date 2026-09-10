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
            join srv in _db.Servicos.AsNoTracking() on c.ServicoId equals srv.Id
            join emp in _db.Empresas.AsNoTracking() on c.EmpresaId equals emp.Id
            select new { Contrato = c, ClienteNome = cli.Nome, ServicoDescricao = srv.Descricao, emp.DiasAlertaReajusteContratoPadrao })
            .FirstOrDefaultAsync(cancellationToken);

        if (resultado is null)
            throw new RecursoNaoEncontradoException($"Contrato {id} não encontrado.");

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var dataProximoReajuste = SituacaoContratoCalculator.CalcularDataProximoReajuste(resultado.Contrato);
        var diasAlertaEfetivo = SituacaoContratoCalculator.DiasAlertaEfetivo(resultado.Contrato, resultado.DiasAlertaReajusteContratoPadrao);
        var situacao = SituacaoContratoCalculator.CalcularSituacao(resultado.Contrato, resultado.DiasAlertaReajusteContratoPadrao, hoje);

        return new ContratoResponse(
            resultado.Contrato.Id, resultado.Contrato.EmpresaId, resultado.Contrato.ClienteId, resultado.ClienteNome,
            resultado.Contrato.ServicoId, resultado.ServicoDescricao, resultado.Contrato.Descricao, resultado.Contrato.ValorAtual,
            resultado.Contrato.DataInicioContrato, resultado.Contrato.PeriodicidadeReajusteMeses, resultado.Contrato.IndiceReajuste,
            resultado.Contrato.DataUltimoReajuste, resultado.Contrato.DiasAlertaOverride, diasAlertaEfetivo,
            dataProximoReajuste, situacao, resultado.Contrato.Ativo, resultado.Contrato.CreatedAt, resultado.Contrato.UpdatedAt);
    }
}
