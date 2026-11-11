namespace NfseSaaS.Application.UseCases.HistoricoValoresContrato;

/// <summary>
/// Percentual de variação gravado no Histórico de valores
/// (MovimentacaoValorContrato.PercentualAplicado, numeric(9,4)).
///
/// Quando o valor anterior era só um marcador (contrato cadastrado com
/// R$ 1 e corrigido para R$ 1.500), a "variação" passa de 100.000% — não
/// significa nada e não cabe na coluna (o banco recusava a gravação inteira
/// com "numeric field overflow"). Nesses casos o histórico guarda os
/// valores anterior e novo e deixa o percentual vazio ("—" na tela).
/// </summary>
public static class PercentualVariacao
{
    /// <summary>Maior percentual que cabe em numeric(9,4).</summary>
    public const decimal Maximo = 99_999.9999m;

    /// <summary>(novo − anterior) / base × 100, 4 casas; null sem base ou fora da coluna.</summary>
    public static decimal? Calcular(decimal valorAnterior, decimal valorNovo, decimal? baseCalculo = null)
    {
        var divisor = baseCalculo ?? valorAnterior;
        if (divisor == 0)
            return null;

        return CabeNaColuna(Math.Round((valorNovo - valorAnterior) / divisor * 100, 4));
    }

    /// <summary>O próprio percentual, ou null se não couber na coluna.</summary>
    public static decimal? CabeNaColuna(decimal? percentual) =>
        percentual is { } p && Math.Abs(p) <= Maximo ? p : null;
}
