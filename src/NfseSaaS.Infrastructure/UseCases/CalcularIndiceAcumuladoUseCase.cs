using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.UseCases.IndicesEconomicos;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class CalcularIndiceAcumuladoUseCase : ICalcularIndiceAcumuladoUseCase
{
    private readonly AppDbContext _db;

    public CalcularIndiceAcumuladoUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IndiceAcumuladoResponse> ExecutarAsync(IndiceReajusteContrato indice, DateOnly dataReferencia, CancellationToken cancellationToken)
    {
        // Só o pedaço que pode cair na janela: 12 meses + a defasagem máxima
        // antes do mês de referência. A regra fica toda na calculadora.
        var mesReferencia = new DateOnly(dataReferencia.Year, dataReferencia.Month, 1);
        var limiteInferior = mesReferencia.AddMonths(-(CalculadoraIndiceAcumulado.MesesJanela + CalculadoraIndiceAcumulado.DefasagemMaximaMeses));

        var serie = await _db.IndicesEconomicosMensais.AsNoTracking()
            .Where(i => i.Indice == indice && i.Competencia >= limiteInferior && i.Competencia < mesReferencia)
            .Select(i => new IndiceAcumuladoMesResponse(i.Competencia, i.Variacao, i.Fonte))
            .ToListAsync(cancellationToken);

        return CalculadoraIndiceAcumulado.Calcular(indice, dataReferencia, serie);
    }
}
