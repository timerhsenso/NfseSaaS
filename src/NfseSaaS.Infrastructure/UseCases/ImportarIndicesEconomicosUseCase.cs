using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.UseCases.IndicesEconomicos;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.IndicesEconomicos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Sempre busca a janela inteira (MesesHistorico, 24 por padrão) até o
/// último mês encerrado: é 1 requisição pequena por índice, e assim a
/// importação se conserta sozinha — preenche buracos, traz de volta um mês
/// excluído e acompanha eventuais revisões do Banco Central. Valores
/// Manuais nunca são tocados. Um índice que falha não impede os outros.
/// </summary>
public sealed class ImportarIndicesEconomicosUseCase : IImportarIndicesEconomicosUseCase
{
    private readonly AppDbContext _db;
    private readonly IFonteIndicesEconomicos _fonte;
    private readonly IndicesEconomicosOptions _options;
    private readonly ILogger<ImportarIndicesEconomicosUseCase> _logger;

    public ImportarIndicesEconomicosUseCase(
        AppDbContext db,
        IFonteIndicesEconomicos fonte,
        IOptions<IndicesEconomicosOptions> options,
        ILogger<ImportarIndicesEconomicosUseCase> logger)
    {
        _db = db;
        _fonte = fonte;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ImportacaoIndicesResponse> ExecutarAsync(CancellationToken cancellationToken)
    {
        var hoje = DataBrasilia.Hoje();
        var fim = new DateOnly(hoje.Year, hoje.Month, 1).AddMonths(-1);
        var inicio = fim.AddMonths(-(Math.Max(_options.MesesHistorico, 1) - 1));

        var resultados = new List<ImportacaoIndiceResultado>();
        foreach (var indice in _fonte.IndicesSuportados)
        {
            try
            {
                resultados.Add(await ImportarIndiceAsync(indice, inicio, fim, cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _db.ChangeTracker.Clear();
                _logger.LogWarning(ex, "Falha ao importar o índice {Indice} do Banco Central.", indice);
                resultados.Add(new ImportacaoIndiceResultado(indice, 0, 0, 0, null, ex.Message));
            }
        }

        _logger.LogInformation(
            "Importação de índices: {Resumo}",
            string.Join("; ", resultados.Select(r => r.Erro is null
                ? $"{r.Indice} +{r.Inseridos} ~{r.Atualizados} manuais={r.ManuaisPreservados} ultimo={r.UltimaCompetencia:yyyy-MM}"
                : $"{r.Indice} ERRO")));

        return new ImportacaoIndicesResponse(resultados);
    }

    private async Task<ImportacaoIndiceResultado> ImportarIndiceAsync(
        IndiceReajusteContrato indice, DateOnly inicio, DateOnly fim, CancellationToken cancellationToken)
    {
        var variacoes = await _fonte.ObterVariacoesAsync(indice, inicio, fim, cancellationToken);

        var existentes = await _db.IndicesEconomicosMensais
            .Where(i => i.Indice == indice && i.Competencia >= inicio && i.Competencia <= fim)
            .ToDictionaryAsync(i => i.Competencia, cancellationToken);

        int inseridos = 0, atualizados = 0, manuais = 0;
        foreach (var variacao in variacoes.Where(v => v.Competencia >= inicio && v.Competencia <= fim))
        {
            if (!existentes.TryGetValue(variacao.Competencia, out var existente))
            {
                _db.IndicesEconomicosMensais.Add(new IndiceEconomicoMensal
                {
                    Indice = indice,
                    Competencia = variacao.Competencia,
                    Variacao = variacao.Variacao,
                    Fonte = FonteIndiceEconomico.BancoCentral
                });
                inseridos++;
            }
            else if (existente.Fonte == FonteIndiceEconomico.Manual)
            {
                manuais++;
            }
            else if (existente.Variacao != variacao.Variacao)
            {
                existente.Variacao = variacao.Variacao;
                atualizados++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        var ultima = await _db.IndicesEconomicosMensais.AsNoTracking()
            .Where(i => i.Indice == indice)
            .MaxAsync(i => (DateOnly?)i.Competencia, cancellationToken);

        return new ImportacaoIndiceResultado(indice, inseridos, atualizados, manuais, ultima, null);
    }
}
