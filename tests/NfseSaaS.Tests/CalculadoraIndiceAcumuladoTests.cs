using Xunit;
using NfseSaaS.Application.UseCases.IndicesEconomicos;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Tests;

public sealed class CalculadoraIndiceAcumuladoTests
{
    private static List<IndiceAcumuladoMesResponse> Serie(DateOnly primeiro, params decimal[] variacoes) =>
        variacoes.Select((v, i) => new IndiceAcumuladoMesResponse(primeiro.AddMonths(i), v, FonteIndiceEconomico.BancoCentral)).ToList();

    [Fact]
    public void Acumular_UsaJurosCompostos()
    {
        // 1,01 × 1,02 = 1,0302 → 3,02% (e não 3% da soma simples)
        Assert.Equal(3.02m, CalculadoraIndiceAcumulado.Acumular(new[] { 1m, 2m }));
    }

    [Fact]
    public void Acumular_DozeMesesDeMeioPorCento()
    {
        // 1,005^12 = 1,0616778118... → 6,1678%
        Assert.Equal(6.1678m, CalculadoraIndiceAcumulado.Acumular(Enumerable.Repeat(0.5m, 12)));
    }

    [Fact]
    public void Calcular_JanelaTerminaNoUltimoMesPublicadoAntesDoAniversario()
    {
        // Dados até out/2026; aniversário em 03/11/2026 → janela nov/2025..out/2026.
        var serie = Serie(new DateOnly(2025, 1, 1), Enumerable.Repeat(0.5m, 22).ToArray());

        var r = CalculadoraIndiceAcumulado.Calcular(IndiceReajusteContrato.Ipca, new DateOnly(2026, 11, 3), serie);

        Assert.True(r.Disponivel);
        Assert.Equal(new DateOnly(2025, 11, 1), r.CompetenciaInicio);
        Assert.Equal(new DateOnly(2026, 10, 1), r.CompetenciaFim);
        Assert.Equal(12, r.Meses.Count);
        Assert.Equal(6.1678m, r.PercentualAcumulado);
    }

    [Fact]
    public void Calcular_IgnoraMesDoAniversarioEPosteriores()
    {
        var serie = Serie(new DateOnly(2025, 10, 1), Enumerable.Repeat(1m, 14).ToArray()); // até nov/2026

        var r = CalculadoraIndiceAcumulado.Calcular(IndiceReajusteContrato.Igpm, new DateOnly(2026, 11, 30), serie);

        Assert.Equal(new DateOnly(2026, 10, 1), r.CompetenciaFim);
    }

    [Fact]
    public void Calcular_OutubroAindaNaoPublicado_JanelaVaiAteSetembro()
    {
        var serie = Serie(new DateOnly(2025, 10, 1), Enumerable.Repeat(0.4m, 12).ToArray()); // out/2025..set/2026

        var r = CalculadoraIndiceAcumulado.Calcular(IndiceReajusteContrato.Ipca, new DateOnly(2026, 11, 3), serie);

        Assert.True(r.Disponivel);
        Assert.Equal(new DateOnly(2025, 10, 1), r.CompetenciaInicio);
        Assert.Equal(new DateOnly(2026, 9, 1), r.CompetenciaFim);
    }

    [Fact]
    public void Calcular_MesFaltandoNaJanela_NaoCalcula()
    {
        var serie = Serie(new DateOnly(2025, 10, 1), Enumerable.Repeat(0.4m, 12).ToArray());
        serie.RemoveAll(m => m.Competencia == new DateOnly(2026, 3, 1));

        var r = CalculadoraIndiceAcumulado.Calcular(IndiceReajusteContrato.Ipca, new DateOnly(2026, 11, 3), serie);

        Assert.False(r.Disponivel);
        Assert.Null(r.PercentualAcumulado);
        Assert.Equal(new[] { new DateOnly(2026, 3, 1) }, r.MesesFaltantes);
        Assert.Contains("03/2026", r.Mensagem);
    }

    [Fact]
    public void Calcular_IndiceDesatualizado_NaoCalcula()
    {
        var serie = Serie(new DateOnly(2025, 1, 1), Enumerable.Repeat(0.4m, 12).ToArray()); // termina dez/2025

        var r = CalculadoraIndiceAcumulado.Calcular(IndiceReajusteContrato.Ipca, new DateOnly(2026, 11, 3), serie);

        Assert.False(r.Disponivel);
        Assert.Contains("desatualizado", r.Mensagem);
    }

    [Fact]
    public void Calcular_SemDados_NaoCalcula()
    {
        var r = CalculadoraIndiceAcumulado.Calcular(IndiceReajusteContrato.Ipca, new DateOnly(2026, 11, 3), new List<IndiceAcumuladoMesResponse>());

        Assert.False(r.Disponivel);
        Assert.Empty(r.Meses);
    }

    [Fact]
    public void Calcular_AcumuladoNegativo()
    {
        var serie = Serie(new DateOnly(2025, 11, 1), Enumerable.Repeat(-0.3m, 12).ToArray());

        var r = CalculadoraIndiceAcumulado.Calcular(IndiceReajusteContrato.Igpm, new DateOnly(2026, 11, 3), serie);

        Assert.True(r.Disponivel);
        Assert.True(r.PercentualAcumulado < 0);
        Assert.Equal(-3.5412m, r.PercentualAcumulado); // 0,997^12 = 0,964588...
    }
}
