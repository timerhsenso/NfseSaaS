using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.IndicesEconomicos;

public sealed record IndiceEconomicoMensalResponse(
    Guid Id,
    IndiceReajusteContrato Indice,
    DateOnly Competencia,
    decimal Variacao,
    FonteIndiceEconomico Fonte,
    DateTimeOffset AtualizadoEm);

/// <summary>Um índice que a tela oferece, com a série de origem (ex.: "SGS 433").</summary>
public sealed record IndiceDisponivelResponse(
    IndiceReajusteContrato Indice,
    string Nome,
    string? Serie,
    DateOnly? UltimaCompetencia);

/// <param name="Disponivel">
/// false quando não dá pra calcular com segurança (mês faltando na janela,
/// índice desatualizado) — <see cref="Mensagem"/> diz o motivo, e
/// <see cref="PercentualAcumulado"/> fica nulo. Nunca um número "quase certo".
/// </param>
public sealed record IndiceAcumuladoResponse(
    IndiceReajusteContrato Indice,
    DateOnly DataReferencia,
    bool Disponivel,
    string? Mensagem,
    DateOnly? CompetenciaInicio,
    DateOnly? CompetenciaFim,
    decimal? PercentualAcumulado,
    IReadOnlyList<IndiceAcumuladoMesResponse> Meses,
    IReadOnlyList<DateOnly> MesesFaltantes);

public sealed record IndiceAcumuladoMesResponse(DateOnly Competencia, decimal Variacao, FonteIndiceEconomico Fonte);

/// <summary>Resultado da importação, por índice — a tela mostra e o job registra no log.</summary>
public sealed record ImportacaoIndicesResponse(IReadOnlyList<ImportacaoIndiceResultado> Indices)
{
    public bool TodosFalharam => Indices.Count > 0 && Indices.All(i => i.Erro is not null);
}

public sealed record ImportacaoIndiceResultado(
    IndiceReajusteContrato Indice,
    int Inseridos,
    int Atualizados,
    int ManuaisPreservados,
    DateOnly? UltimaCompetencia,
    string? Erro);
