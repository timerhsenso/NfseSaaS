using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.HistoricoValoresContrato;
using NfseSaaS.Infrastructure.Contratos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ListarHistoricoValoresContratoUseCase : IListarHistoricoValoresContratoUseCase
{
    private readonly AppDbContext _db;

    public ListarHistoricoValoresContratoUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<MovimentacaoValorContratoResponse>> ExecutarAsync(Guid contratoId, CancellationToken cancellationToken)
    {
        var contratoExiste = await _db.Contratos.AnyAsync(c => c.Id == contratoId, cancellationToken);
        if (!contratoExiste)
            throw new RecursoNaoEncontradoException($"Contrato {contratoId} não encontrado.");

        // Materializa antes de mapear: a composição é JSON desserializado em
        // memória (não traduz pra SQL).
        var eventos = await _db.MovimentacoesValorContrato.AsNoTracking()
            .Where(m => m.ContratoId == contratoId)
            .OrderByDescending(m => m.DataVigencia).ThenByDescending(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        return eventos
            .Select(m => new MovimentacaoValorContratoResponse(
                m.Id, m.ContratoId, m.Tipo, m.Status, m.DataVigencia, m.ValorAnterior, m.ValorNovo,
                m.PercentualCalculado, m.PercentualAplicado, m.Indice, m.CompetenciaIndiceInicio, m.CompetenciaIndiceFim,
                m.Motivo, ComposicaoContrato.Desserializar(m.ComposicaoJson), m.Automatico, m.CreatedAt))
            .ToList();
    }
}
