using NfseSaaS.Application.UseCases.HistoricoValoresContrato;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>Percentual do Histórico de valores: nunca estoura numeric(9,4).</summary>
public class PercentualVariacaoTests
{
    [Fact]
    public void Calcula_a_variacao_com_4_casas()
    {
        Assert.Equal(4.6812m, PercentualVariacao.Calcular(1000m, 1046.812m));
        Assert.Equal(-10m, PercentualVariacao.Calcular(100m, 90m));
        Assert.Equal(12.683m, PercentualVariacao.Calcular(1500m, 1626.83m, baseCalculo: 1000m));
    }

    [Fact]
    public void Valor_marcador_de_R_1_nao_vira_percentual()
    {
        // Contrato cadastrado com R$ 1 e corrigido para R$ 1.500 / R$ 2.000.
        Assert.Null(PercentualVariacao.Calcular(1m, 2000m));
        Assert.Null(PercentualVariacao.Calcular(0.01m, 1500m));
        Assert.Equal(74_950m, PercentualVariacao.Calcular(2m, 1501m));
    }

    [Fact]
    public void Sem_base_nao_ha_percentual()
    {
        Assert.Null(PercentualVariacao.Calcular(0m, 100m));
        Assert.Null(PercentualVariacao.CabeNaColuna(null));
        Assert.Null(PercentualVariacao.CabeNaColuna(100_000m));
        Assert.Equal(PercentualVariacao.Maximo, PercentualVariacao.CabeNaColuna(PercentualVariacao.Maximo));
    }
}
