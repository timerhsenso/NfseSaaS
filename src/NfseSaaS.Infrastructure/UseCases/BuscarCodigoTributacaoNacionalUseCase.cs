using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.UseCases.Catalogos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class BuscarCodigoTributacaoNacionalUseCase : IBuscarCodigoTributacaoNacionalUseCase
{
    private readonly AppDbContext _db;

    public BuscarCodigoTributacaoNacionalUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<CodigoTributacaoNacionalResponse>> BuscarAsync(string? termo, int limite, CancellationToken cancellationToken)
    {
        var query = _db.CodigosTributacaoNacional.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(termo))
        {
            // EF.Functions.ILike, não .Contains(): .Contains() vira LIKE
            // puro no Postgres, que é CASE-SENSITIVE por padrão — "suporte"
            // (minúsculo, como o usuário digita) não batia com "Suporte
            // técnico..." (maiúscula por ser início de frase). ILIKE é a
            // versão case-insensitive do Postgres pra isso. Escapa % e _
            // do termo digitado antes de embrulhar em '%...%', senão um
            // usuário pesquisando por esses caracteres literalmente teria
            // resultado errado (viram curinga do LIKE, não texto).
            var padrao = $"%{EscaparCoringasLike(termo)}%";
            query = query.Where(c => EF.Functions.ILike(c.Codigo, padrao) || EF.Functions.ILike(c.Descricao, padrao));
        }

        return await query
            .OrderBy(c => c.Codigo)
            .Take(limite)
            .Select(c => new CodigoTributacaoNacionalResponse(c.Codigo, c.Descricao))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExisteAsync(string codigo, CancellationToken cancellationToken) =>
        _db.CodigosTributacaoNacional.AsNoTracking().AnyAsync(c => c.Codigo == codigo, cancellationToken);

    private static string EscaparCoringasLike(string termo) =>
        termo.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
