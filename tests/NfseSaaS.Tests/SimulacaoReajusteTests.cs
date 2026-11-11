using NfseSaaS.Application.UseCases.ReajustesContrato;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Contratos;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>Reajuste só na parte fixa do contrato e data sugerida da tela.</summary>
public class SimulacaoReajusteTests
{
    private static ContratoServico Linha(decimal valor, bool variavel = false, decimal quantidade = 1) =>
        new() { Quantidade = quantidade, ValorUnitario = valor, ValorVariavel = variavel };

    [Fact]
    public void Linha_variavel_fica_fora_do_reajuste()
    {
        var linhas = new[] { Linha(1000m), Linha(500m, variavel: true) };
        var variavel = ReajusteContrato.ParteVariavel(linhas);

        Assert.Equal(500m, variavel);
        Assert.Equal(1626.83m, ReajusteContrato.CalcularValorNovo(1500m, variavel, 12.6825m));
    }

    [Fact]
    public void Contrato_so_com_linhas_variaveis_reajusta_tudo()
    {
        var linhas = new[] { Linha(300m, variavel: true), Linha(200m, variavel: true) };

        Assert.Equal(0m, ReajusteContrato.ParteVariavel(linhas));
        Assert.Equal(550m, ReajusteContrato.CalcularValorNovo(500m, 0m, 10m));
    }

    [Fact]
    public void Contrato_sem_variavel_e_igual_ao_calculo_de_antes()
    {
        var linhas = new[] { Linha(100m, quantidade: 2), Linha(50m) };

        Assert.Equal(0m, ReajusteContrato.ParteVariavel(linhas));
        Assert.Equal(ReajusteContrato.CalcularValor(250m, 4.5m), ReajusteContrato.CalcularValorNovo(250m, 0m, 4.5m));
    }

    [Theory]
    [InlineData("2026-11-01", "2026-11-11", "2026-11-01")] // vencido → aniversário
    [InlineData("2026-12-20", "2026-11-11", "2026-12-20")] // chegando (até 45 dias) → aniversário
    [InlineData("2027-03-01", "2026-11-11", "2026-11-11")] // longe → hoje
    public void Data_sugerida_e_o_aniversario_proximo_ou_vencido(string aniversario, string hoje, string esperada)
    {
        Assert.Equal(DateOnly.Parse(esperada), DataSugeridaReajuste.Calcular(DateOnly.Parse(aniversario), DateOnly.Parse(hoje)));
    }

    [Fact]
    public void Sem_aniversario_sugere_hoje()
    {
        var hoje = new DateOnly(2026, 11, 11);
        Assert.Equal(hoje, DataSugeridaReajuste.Calcular(null, hoje));
    }
}
