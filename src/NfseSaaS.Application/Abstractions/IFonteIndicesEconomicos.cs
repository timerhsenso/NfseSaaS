using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.Abstractions;

/// <summary>
/// Fonte externa das variações mensais dos índices (hoje: API SGS do Banco
/// Central). Abstraída para a importação ser testável sem rede.
/// </summary>
public interface IFonteIndicesEconomicos
{
    /// <summary>Índices que esta fonte sabe buscar (o "Outro" nunca entra).</summary>
    IReadOnlyCollection<IndiceReajusteContrato> IndicesSuportados { get; }

    /// <summary>Código da série na fonte (ex.: "SGS 433"), para exibir na tela.</summary>
    string? DescreverSerie(IndiceReajusteContrato indice);

    /// <summary>
    /// Variações publicadas com competência entre <paramref name="inicio"/> e
    /// <paramref name="fim"/> (inclusive). Lista vazia quando ainda não há
    /// nada publicado no período — não é erro.
    /// </summary>
    Task<IReadOnlyList<VariacaoMensalIndice>> ObterVariacoesAsync(
        IndiceReajusteContrato indice, DateOnly inicio, DateOnly fim, CancellationToken cancellationToken);
}

/// <param name="Competencia">Sempre o dia 1 do mês.</param>
/// <param name="Variacao">Em percentual (0,56 = 0,56%).</param>
public sealed record VariacaoMensalIndice(DateOnly Competencia, decimal Variacao);
