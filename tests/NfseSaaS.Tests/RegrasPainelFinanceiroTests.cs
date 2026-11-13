using NfseSaaS.Application.UseCases.PainelFinanceiro;
using NfseSaaS.Domain.Enums;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>DB1: contas do painel financeiro (meses, 12 meses, maiores clientes, limite do MEI e faixa do Simples).</summary>
public class RegrasPainelFinanceiroTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 13);
    private static readonly Guid ClienteA = Guid.NewGuid();
    private static readonly Guid ClienteB = Guid.NewGuid();

    private static NotaPainel Nota(int ano, int mes, decimal valor, bool contrato = true, Guid? cliente = null) =>
        new(new DateOnly(ano, mes, 10), cliente ?? ClienteA, contrato, valor);

    [Fact]
    public void Grafico_tem_sempre_12_meses_ate_o_atual_separando_contratos_e_avulsas()
    {
        var meses = RegrasPainelFinanceiro.PorMes(new[]
        {
            Nota(2026, 11, 100), Nota(2026, 11, 50, contrato: false), Nota(2026, 3, 70), Nota(2025, 11, 999)
        }, Hoje);

        Assert.Equal(12, meses.Count);
        Assert.Equal(new DateOnly(2025, 12, 1), meses[0].Competencia);
        Assert.Equal(new DateOnly(2026, 11, 1), meses[^1].Competencia);
        Assert.Equal(100m, meses[^1].Contratos);
        Assert.Equal(50m, meses[^1].Avulsas);
        Assert.Equal(150m, meses[^1].Total);
        Assert.Equal(2, meses[^1].Notas);
        Assert.Equal(70m, meses.Single(m => m.Competencia.Month == 3).Total);
        Assert.Equal(0, meses.Single(m => m.Competencia.Month == 5).Notas);
    }

    [Fact]
    public void Ultimos_12_meses_sao_os_fechados_antes_do_mes_atual()
    {
        var notas = new[] { Nota(2025, 10, 1), Nota(2025, 11, 10), Nota(2026, 10, 20), Nota(2026, 11, 300) };

        Assert.Equal(30m, RegrasPainelFinanceiro.ReceitaUltimos12Meses(notas, Hoje));
        Assert.Equal(new DateOnly(2025, 11, 1), RegrasPainelFinanceiro.InicioJanela(Hoje));
    }

    [Fact]
    public void Maiores_clientes_em_ordem_com_a_fatia_de_cada_um()
    {
        var nomes = new Dictionary<Guid, string> { [ClienteA] = "HOLTZ", [ClienteB] = "COPENOR" };
        var maiores = RegrasPainelFinanceiro.Maiores(new[]
        {
            Nota(2026, 10, 100, cliente: ClienteA), Nota(2026, 11, 300, cliente: ClienteB), Nota(2026, 9, 0, cliente: ClienteA)
        }, nomes, out var total);

        Assert.Equal(400m, total);
        Assert.Equal(new[] { "COPENOR", "HOLTZ" }, maiores.Select(m => m.Nome));
        Assert.Equal(75m, maiores[0].Percentual);
        Assert.Empty(RegrasPainelFinanceiro.Maiores(Array.Empty<NotaPainel>(), nomes, out _));
    }

    [Theory]
    [InlineData(40_000, "ok")]
    [InlineData(70_000, "atencao")]
    [InlineData(90_000, "alerta")]
    [InlineData(100_000, "alerta")]
    public void Limite_do_mei_pelo_ano(decimal acumulado, string nivel)
    {
        var limite = RegrasPainelFinanceiro.LimiteDoMei(acumulado, 2026);

        Assert.Equal("mei", limite.Tipo);
        Assert.Equal(81_000m, limite.Limite);
        Assert.Equal(nivel, limite.Nivel);
        if (acumulado > 97_200)
            Assert.Contains("retroativo", limite.Texto);
        else if (acumulado > 81_000)
            Assert.Contains("janeiro", limite.Texto);
    }

    [Theory]
    [InlineData(100_000, 180_000, "1ª faixa", "ok")]
    [InlineData(170_000, 180_000, "1ª faixa", "atencao")]
    [InlineData(200_000, 360_000, "2ª faixa", "ok")]
    [InlineData(3_700_000, 4_800_000, "6ª faixa", "alerta")]
    public void Faixa_do_simples_pelos_12_meses(decimal rbt12, decimal teto, string faixa, string nivel)
    {
        var limite = RegrasPainelFinanceiro.FaixaDoSimples(rbt12);

        Assert.Equal(teto, limite.Limite);
        Assert.StartsWith(faixa, limite.Texto);
        Assert.Equal(nivel, limite.Nivel);
    }

    [Fact]
    public void Limite_so_para_mei_e_simples()
    {
        Assert.Equal("mei", RegrasPainelFinanceiro.Limite("2", 1, 1, 2026)!.Tipo);
        Assert.Equal("simples", RegrasPainelFinanceiro.Limite("3", 1, 1, 2026)!.Tipo);
        Assert.Null(RegrasPainelFinanceiro.Limite("1", 1, 1, 2026));
        Assert.Equal("alerta", RegrasPainelFinanceiro.FaixaDoSimples(5_000_000).Nivel);
    }

    [Fact]
    public void Escopo_padrao_e_vigencia_e_janela_dos_reajustes()
    {
        Assert.Equal(EscopoPainelFinanceiro.Empresa, RegrasPainelFinanceiro.EscopoPadrao(PerfilConta.Contador));
        Assert.Equal(EscopoPainelFinanceiro.Todas, RegrasPainelFinanceiro.EscopoPadrao(PerfilConta.Empresa));

        var novembro = new DateOnly(2026, 11, 1);
        Assert.True(RegrasPainelFinanceiro.VigenteNoMes(new DateOnly(2026, 11, 30), null, novembro));
        Assert.False(RegrasPainelFinanceiro.VigenteNoMes(new DateOnly(2026, 12, 1), null, novembro));
        Assert.True(RegrasPainelFinanceiro.VigenteNoMes(new DateOnly(2025, 1, 1), new DateOnly(2026, 11, 1), novembro));
        Assert.False(RegrasPainelFinanceiro.VigenteNoMes(new DateOnly(2025, 1, 1), new DateOnly(2026, 10, 31), novembro));

        Assert.Equal(new DateOnly(2026, 12, 31), RegrasPainelFinanceiro.ReajustesAte(Hoje));
        Assert.Equal(new DateOnly(2027, 1, 31), RegrasPainelFinanceiro.ReajustesAte(new DateOnly(2026, 12, 31)));
        Assert.Equal("IGP-M", RegrasPainelFinanceiro.NomeIndice(IndiceReajusteContrato.Igpm));
    }
}
