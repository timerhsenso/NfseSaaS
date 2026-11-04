using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.UseCases.IndicesEconomicos;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ListarIndicesEconomicosUseCase : IListarIndicesEconomicosUseCase
{
    private readonly AppDbContext _db;

    public ListarIndicesEconomicosUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<IndiceEconomicoMensalResponse>> ExecutarAsync(IndiceReajusteContrato indice, CancellationToken cancellationToken)
    {
        return await _db.IndicesEconomicosMensais.AsNoTracking()
            .Where(i => i.Indice == indice)
            .OrderByDescending(i => i.Competencia)
            .Select(i => new IndiceEconomicoMensalResponse(
                i.Id, i.Indice, i.Competencia, i.Variacao, i.Fonte, i.UpdatedAt ?? i.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
