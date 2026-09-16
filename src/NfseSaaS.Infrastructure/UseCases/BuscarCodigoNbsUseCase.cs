using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.UseCases.Catalogos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class BuscarCodigoNbsUseCase : IBuscarCodigoNbsUseCase
{
    private readonly AppDbContext _db;

    public BuscarCodigoNbsUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<CodigoNbsResponse>> BuscarAsync(string? termo, int limite, CancellationToken cancellationToken)
    {
        var query = _db.CodigosNbs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(termo))
        {
            // Ver comentário completo em BuscarCodigoTributacaoNacionalUseCase
            // — .Contains() vira LIKE case-sensitive no Postgres, não ILIKE.
            var padrao = $"%{EscaparCoringasLike(termo)}%";
            query = query.Where(c => EF.Functions.ILike(c.Codigo, padrao) || EF.Functions.ILike(c.Descricao, padrao));
        }

        return await query
            .OrderBy(c => c.Codigo)
            .Take(limite)
            .Select(c => new CodigoNbsResponse(c.Codigo, c.Descricao))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExisteAsync(string codigo, CancellationToken cancellationToken) =>
        _db.CodigosNbs.AsNoTracking().AnyAsync(c => c.Codigo == codigo, cancellationToken);

    private static string EscaparCoringasLike(string termo) =>
        termo.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
