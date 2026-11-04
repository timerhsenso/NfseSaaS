using System.Globalization;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.IndicesEconomicos;

/// <summary>
/// Acumulado de 12 meses de um índice para uma data de aniversário.
///
/// Janela: os 12 últimos meses JÁ PUBLICADOS na data de referência — é o
/// que se consegue saber no dia do aniversário (o IPCA de outubro só sai
/// por volta do dia 10 de novembro; num aniversário em 03/11 a janela
/// termina em setembro). Concretamente: o fim é a competência mais recente
/// que existe na tabela ANTES do mês da data de referência; o início, 11
/// meses antes dele.
///
/// Fórmula: juros compostos, Π(1 + vᵢ/100) − 1, em percentual com 4 casas
/// (mesma precisão de PercentualCalculado no histórico de valores).
///
/// Recusa calcular (Disponivel=false) em vez de devolver um número
/// enganoso quando: falta algum mês dentro da janela, ou o último valor
/// publicado está velho demais (importação parada) — aí o reajuste
/// precisa de alguém olhando.
/// </summary>
public static class CalculadoraIndiceAcumulado
{
    public const int MesesJanela = 12;

    /// <summary>
    /// Quantos meses atrás do mês de referência o último valor pode estar.
    /// Aniversário em novembro: aceita fim em agosto, setembro ou outubro.
    /// O IGP-M e o IPCA nunca atrasam mais que 1–2 meses; 3 dá folga para
    /// atraso de publicação sem aceitar uma importação parada há meses.
    /// </summary>
    public const int DefasagemMaximaMeses = 3;

    public static IndiceAcumuladoResponse Calcular(
        IndiceReajusteContrato indice,
        DateOnly dataReferencia,
        IReadOnlyCollection<IndiceAcumuladoMesResponse> serie)
    {
        var mesReferencia = new DateOnly(dataReferencia.Year, dataReferencia.Month, 1);
        var porCompetencia = serie
            .Where(m => m.Competencia < mesReferencia)
            .GroupBy(m => m.Competencia)
            .ToDictionary(g => g.Key, g => g.First());

        if (porCompetencia.Count == 0)
            return Indisponivel(indice, dataReferencia, null, null,
                $"Não há variação publicada antes de {Mes(mesReferencia)}.", Array.Empty<DateOnly>());

        var fim = porCompetencia.Keys.Max();
        var inicio = fim.AddMonths(-(MesesJanela - 1));

        if (fim < mesReferencia.AddMonths(-DefasagemMaximaMeses))
            return Indisponivel(indice, dataReferencia, inicio, fim,
                $"Índice desatualizado: o último mês disponível é {Mes(fim)}. Importe ou lance os meses que faltam.",
                Array.Empty<DateOnly>());

        var meses = new List<IndiceAcumuladoMesResponse>(MesesJanela);
        var faltantes = new List<DateOnly>();
        for (var competencia = inicio; competencia <= fim; competencia = competencia.AddMonths(1))
        {
            if (porCompetencia.TryGetValue(competencia, out var mes))
                meses.Add(mes);
            else
                faltantes.Add(competencia);
        }

        if (faltantes.Count > 0)
            return Indisponivel(indice, dataReferencia, inicio, fim,
                $"Faltam {faltantes.Count} mês(es) na janela de {Mes(inicio)} a {Mes(fim)}: " +
                string.Join(", ", faltantes.Select(f => Mes(f))) + ".",
                faltantes, meses);

        return new IndiceAcumuladoResponse(
            indice, dataReferencia, true, null, inicio, fim,
            Acumular(meses.Select(m => m.Variacao)), meses, Array.Empty<DateOnly>());
    }

    /// <summary>Π(1 + vᵢ/100) − 1, em percentual, 4 casas (arredondamento comercial).</summary>
    public static decimal Acumular(IEnumerable<decimal> variacoesPercentuais)
    {
        var fator = 1m;
        foreach (var variacao in variacoesPercentuais)
            fator *= 1m + variacao / 100m;

        return decimal.Round((fator - 1m) * 100m, 4, MidpointRounding.AwayFromZero);
    }

    private static string Mes(DateOnly competencia) => competencia.ToString("MM/yyyy", CultureInfo.InvariantCulture);

    private static IndiceAcumuladoResponse Indisponivel(
        IndiceReajusteContrato indice, DateOnly dataReferencia, DateOnly? inicio, DateOnly? fim,
        string mensagem, IReadOnlyList<DateOnly> faltantes, IReadOnlyList<IndiceAcumuladoMesResponse>? meses = null)
        => new(indice, dataReferencia, false, mensagem, inicio, fim, null,
            meses ?? Array.Empty<IndiceAcumuladoMesResponse>(), faltantes);
}
