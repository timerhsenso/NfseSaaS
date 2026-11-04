using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.IndicesEconomicos;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ListarIndicesDisponiveisUseCase : IListarIndicesDisponiveisUseCase
{
    private static readonly Dictionary<IndiceReajusteContrato, string> Nomes = new()
    {
        [IndiceReajusteContrato.Ipca] = "IPCA",
        [IndiceReajusteContrato.Igpm] = "IGP-M",
        [IndiceReajusteContrato.Incc] = "INCC",
        [IndiceReajusteContrato.Inpc] = "INPC",
        [IndiceReajusteContrato.Selic] = "Selic",
        [IndiceReajusteContrato.Cdi] = "CDI"
    };

    private readonly AppDbContext _db;
    private readonly IFonteIndicesEconomicos _fonte;

    public ListarIndicesDisponiveisUseCase(AppDbContext db, IFonteIndicesEconomicos fonte)
    {
        _db = db;
        _fonte = fonte;
    }

    public async Task<IReadOnlyList<IndiceDisponivelResponse>> ExecutarAsync(CancellationToken cancellationToken)
    {
        var ultimas = await _db.IndicesEconomicosMensais.AsNoTracking()
            .GroupBy(i => i.Indice)
            .Select(g => new { Indice = g.Key, Ultima = g.Max(i => i.Competencia) })
            .ToDictionaryAsync(x => x.Indice, x => x.Ultima, cancellationToken);

        return Nomes
            .Select(n => new IndiceDisponivelResponse(
                n.Key, n.Value, _fonte.DescreverSerie(n.Key),
                ultimas.TryGetValue(n.Key, out var ultima) ? ultima : null))
            .ToList();
    }
}
