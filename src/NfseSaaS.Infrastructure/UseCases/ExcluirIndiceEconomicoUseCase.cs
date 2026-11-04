using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.IndicesEconomicos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ExcluirIndiceEconomicoUseCase : IExcluirIndiceEconomicoUseCase
{
    private readonly AppDbContext _db;

    public ExcluirIndiceEconomicoUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task ExecutarAsync(Guid id, CancellationToken cancellationToken)
    {
        // Seguro: os reajustes guardam o percentual e a janela usados
        // (PercentualCalculado, CompetenciaIndiceInicio/Fim) — não dependem
        // desta tabela depois de registrados.
        var excluidos = await _db.IndicesEconomicosMensais
            .Where(i => i.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        if (excluidos == 0)
            throw new RecursoNaoEncontradoException($"Variação {id} não encontrada.");
    }
}
