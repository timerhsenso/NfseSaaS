using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.ReajustesContrato;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ListarReajustesUseCase : IListarReajustesUseCase
{
    private readonly AppDbContext _db;

    public ListarReajustesUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ReajusteContratoResponse>> ExecutarAsync(Guid contratoId, CancellationToken cancellationToken)
    {
        var contratoExiste = await _db.Contratos.AnyAsync(c => c.Id == contratoId, cancellationToken);
        if (!contratoExiste)
            throw new RecursoNaoEncontradoException($"Contrato {contratoId} não encontrado.");

        return await _db.ReajustesContrato.AsNoTracking()
            .Where(r => r.ContratoId == contratoId)
            .OrderByDescending(r => r.DataReajuste).ThenByDescending(r => r.CreatedAt)
            .Select(r => new ReajusteContratoResponse(
                r.Id, r.ContratoId, r.DataReajuste, r.ValorAnterior, r.ValorNovo,
                r.PercentualAplicado, r.IndiceUsado, r.Observacao, r.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
