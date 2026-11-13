using System.Xml.Linq;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.Fiscal.Retencoes;
using NfseSaaS.Nacional.Models;
using NfseSaaS.Nacional.Options;
using NfseSaaS.Nacional.Validation;
using NfseSaaS.Tests.TestData;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>C6: descontos e retenções federais (regras puras, DPS e XSD).</summary>
public class RetencoesFederaisTests
{
    private const string Ns = "http://www.sped.fazenda.gov.br/nfse";

    private static readonly PercentuaisRetencao Todas = new(
        RegrasRetencoesFederais.PadraoIrrf, RegrasRetencoesFederais.PadraoPis, RegrasRetencoesFederais.PadraoCofins,
        RegrasRetencoesFederais.PadraoCsll, RegrasRetencoesFederais.PadraoInss);

    private static EntradaRetencoes Entrada(decimal valor, PercentuaisRetencao p, string opSimpNac = "1",
        decimal descInc = 0, decimal descCond = 0, bool issRetido = false, decimal? aliquotaIss = null, bool reterAbaixo = false) =>
        new(valor, descInc, descCond, p, opSimpNac, issRetido, aliquotaIss, reterAbaixo);

    [Theory]
    [InlineData(false, false, false, "0")]
    [InlineData(true, true, true, "3")]
    [InlineData(true, true, false, "4")]
    [InlineData(true, false, false, "5")]
    [InlineData(false, true, false, "6")]
    [InlineData(false, true, true, "7")]
    [InlineData(false, false, true, "8")]
    [InlineData(true, false, true, "9")]
    public void TpRetPisCofins_segue_a_tabela_oficial(bool pis, bool cofins, bool csll, string esperado) =>
        Assert.Equal(esperado, RegrasRetencoesFederais.TpRetPisCofins(pis, cofins, csll));

    [Fact]
    public void Calcula_as_retencoes_do_lucro_presumido_sobre_10_mil()
    {
        var r = RegrasRetencoesFederais.Calcular(Entrada(10_000m, Todas));

        Assert.Equal(150.00m, r.Irrf);
        Assert.Equal(65.00m, r.Pis);
        Assert.Equal(300.00m, r.Cofins);
        Assert.Equal(100.00m, r.Csll);
        Assert.Equal(1_100.00m, r.Inss);
        Assert.Equal(465.00m, r.RetidoCsllTotal);
        Assert.Equal("3", r.TpRetPisCofins);
        Assert.Equal(1_715.00m, r.TotalRetencoes);
        Assert.Equal(8_285.00m, r.ValorLiquido);
        Assert.Empty(r.Alertas);
    }

    [Fact]
    public void Desconto_incondicionado_reduz_a_base_e_o_condicionado_so_o_liquido()
    {
        var r = RegrasRetencoesFederais.Calcular(Entrada(1_000m, new PercentuaisRetencao(Irrf: 1.5m), descInc: 200m, descCond: 50m,
            issRetido: true, aliquotaIss: 5m, reterAbaixo: true));

        Assert.Equal(800m, r.BaseCalculo);
        Assert.Equal(12.00m, r.Irrf);
        Assert.Equal(40.00m, r.IssRetido);
        // 1000 − 200 − 50 − 12 − 40
        Assert.Equal(698.00m, r.ValorLiquido);
    }

    [Fact]
    public void Dispensa_de_valor_minimo_zera_e_avisa()
    {
        // 200 × 1,5% = 3,00 (IRRF) e 200 × 4,65% = 9,30 (PIS/COFINS/CSLL)
        var r = RegrasRetencoesFederais.Calcular(Entrada(200m, Todas with { Inss = null }));

        Assert.Equal(0m, r.Irrf);
        Assert.Equal(0m, r.RetidoCsllTotal);
        Assert.Equal("0", r.TpRetPisCofins);
        Assert.Equal(2, r.Dispensas.Count);
        Assert.Equal(200m, r.ValorLiquido);
    }

    [Fact]
    public void Exatamente_10_reais_tambem_e_dispensado_e_o_usuario_pode_reter_mesmo_assim()
    {
        var p = new PercentuaisRetencao(Irrf: 1m);

        Assert.Equal(0m, RegrasRetencoesFederais.Calcular(Entrada(1_000m, p)).Irrf);
        Assert.Equal(10.00m, RegrasRetencoesFederais.Calcular(Entrada(1_000m, p, reterAbaixo: true)).Irrf);
        Assert.Equal(10.01m, RegrasRetencoesFederais.Calcular(Entrada(1_001m, p)).Irrf);
    }

    [Fact]
    public void Inss_e_dispensado_so_abaixo_de_10()
    {
        Assert.Equal(0m, RegrasRetencoesFederais.Calcular(Entrada(90m, new PercentuaisRetencao(Inss: 11m))).Inss);
        Assert.Equal(11.00m, RegrasRetencoesFederais.Calcular(Entrada(100m, new PercentuaisRetencao(Inss: 11m))).Inss);
    }

    [Fact]
    public void Automacao_nao_aplica_o_que_o_regime_dispensa()
    {
        Assert.Equal(Todas, RegrasRetencoesFederais.PadraoParaRegime(Todas, "1"));
        Assert.Equal(new PercentuaisRetencao(Inss: 11m), RegrasRetencoesFederais.PadraoParaRegime(Todas, "3"));
        Assert.Equal(PercentuaisRetencao.Nenhum, RegrasRetencoesFederais.PadraoParaRegime(Todas, "2"));
        Assert.Equal(PercentuaisRetencao.Nenhum, RegrasRetencoesFederais.PadraoParaRegime(null, "1"));
    }

    [Fact]
    public void Simples_marcando_retencao_na_mao_gera_alertas_sem_bloquear()
    {
        var r = RegrasRetencoesFederais.Calcular(Entrada(10_000m, Todas, opSimpNac: "3"));

        Assert.Equal(3, r.Alertas.Count);
        Assert.Contains(r.Alertas, a => a.Contains("765/2007"));
        Assert.Contains(r.Alertas, a => a.Contains("459/2004"));
        Assert.Contains(r.Alertas, a => a.Contains("Anexo IV"));
        Assert.Equal(150m, r.Irrf);
    }

    [Fact]
    public void Mei_com_retencao_avisa()
    {
        var alertas = RegrasRetencoesFederais.AlertasRegime(new PercentuaisRetencao(Irrf: 1.5m), "2");
        Assert.Single(alertas);
        Assert.Contains("MEI", alertas[0]);
        Assert.Empty(RegrasRetencoesFederais.AlertasRegime(PercentuaisRetencao.Nenhum, "2"));
    }

    [Theory]
    [InlineData(100, 0, "incondicionado")]
    [InlineData(60, 40, "somados")]
    [InlineData(-1, 0, "negativo")]
    public void Descontos_invalidos_sao_barrados(decimal descInc, decimal descCond, string trecho)
    {
        var ex = Assert.Throws<RegraNegocioException>(() =>
            RegrasRetencoesFederais.Validar(100m, descInc, descCond, PercentuaisRetencao.Nenhum));
        Assert.Contains(trecho, ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100.01)]
    [InlineData(1.555)]
    public void Percentual_invalido_e_barrado(decimal percentual) =>
        Assert.Throws<RegraNegocioException>(() => RegrasRetencoesFederais.ValidarPercentuais(new PercentuaisRetencao(Irrf: percentual)));

    [Fact]
    public void Liquido_zero_ou_negativo_e_barrado()
    {
        var ex = Assert.Throws<RegraNegocioException>(() =>
            RegrasRetencoesFederais.CalcularValidado(Entrada(100m, new PercentuaisRetencao(Irrf: 60m, Inss: 50m))));
        Assert.Contains("líquido", ex.Message);
    }

    [Fact]
    public void Frase_de_dispensa_so_para_simples_sem_retencao_e_sem_repetir()
    {
        const string descricao = "Consultoria de outubro";

        var comFrase = RegrasRetencoesFederais.DescricaoComFraseDispensa(descricao, true, "3", temRetencaoFederal: false);
        Assert.EndsWith(RegrasRetencoesFederais.FraseDispensaSimples, comFrase);
        Assert.Equal(comFrase, RegrasRetencoesFederais.DescricaoComFraseDispensa(comFrase, true, "3", false));

        Assert.Equal(descricao, RegrasRetencoesFederais.DescricaoComFraseDispensa(descricao, false, "3", false));
        Assert.Equal(descricao, RegrasRetencoesFederais.DescricaoComFraseDispensa(descricao, true, "1", false));
        Assert.Equal(descricao, RegrasRetencoesFederais.DescricaoComFraseDispensa(descricao, true, "3", true));

        var longa = new string('x', 1990);
        Assert.Equal(longa, RegrasRetencoesFederais.DescricaoComFraseDispensa(longa, true, "3", false));
    }

    [Fact]
    public void Percentual_de_um_valor_retido_para_reabrir_a_nota()
    {
        Assert.Equal(1.50m, RegrasRetencoesFederais.PercentualDe(150m, 10_000m));
        Assert.Null(RegrasRetencoesFederais.PercentualDe(null, 10_000m));
        Assert.Null(RegrasRetencoesFederais.PercentualDe(0m, 10_000m));
    }

    // ----- DPS -----

    private static DpsRequest RequestComRetencoes() => DpsRequestFactory.CriarRequestDeTeste() with
    {
        Valor = 10_000m,
        Prestador = DpsRequestFactory.CriarRequestDeTeste().Prestador with { OpSimpNac = "1" },
        Tributacao = DpsRequestFactory.CriarRequestDeTeste().Tributacao with
        {
            CstPisCofins = "00",
            TpRetPisCofins = "3",
            RetencoesFederais = new RetencoesFederaisDps(VRetCP: 1_100m, VRetIRRF: 150m, VRetCSLL: 465m)
        },
        Descontos = new DescontosDps(Incondicionado: 200m, Condicionado: 50m)
    };

    [Theory]
    [InlineData(LeiauteDps.Nt009)]
    [InlineData(LeiauteDps.Nt004)]
    public void Dps_com_descontos_e_retencoes_e_valida_no_XSD(LeiauteDps leiaute)
    {
        var (xml, _) = DpsBuilderFactory.Criar(leiaute).Construir(RequestComRetencoes());

        var erros = DpsXsdValidator.Validar(xml, DpsXsdValidator.PastaProducaoRestrita20261020);
        if (leiaute == LeiauteDps.Nt009)
            Assert.True(erros.Count == 0, "DPS inválida no XSD:\n" + string.Join("\n", erros));

        var doc = XDocument.Parse(xml);
        Assert.Equal(new[] { "piscofins", "vRetCP", "vRetIRRF", "vRetCSLL" }, Filhos(doc, "tribFed"));
        Assert.Equal(new[] { "vDescIncond", "vDescCond" }, Filhos(doc, "vDescCondIncond"));
        Assert.Equal(new[] { "vServPrest", "vDescCondIncond", "trib" }, Filhos(doc, "valores").Take(3));
        Assert.Equal("465.00", Valor(doc, "vRetCSLL"));
        Assert.Equal("3", Valor(doc, "tpRetPisCofins"));
        Assert.Equal("200.00", Valor(doc, "vDescIncond"));
    }

    [Fact]
    public void Dps_sem_retencao_e_sem_desconto_nao_escreve_os_grupos()
    {
        var (xml, _) = DpsBuilderFactory.Criar(LeiauteDps.Nt009).Construir(DpsRequestFactory.CriarRequestDeTeste() with
        {
            Tributacao = DpsRequestFactory.CriarRequestDeTeste().Tributacao with { RetencoesFederais = new RetencoesFederaisDps(null, 0m, null) },
            Descontos = new DescontosDps(null, 0m)
        });

        Assert.DoesNotContain("vDescCondIncond", xml);
        Assert.DoesNotContain("vRetIRRF", xml);
        Assert.Equal(new[] { "piscofins" }, Filhos(XDocument.Parse(xml), "tribFed"));
    }

    [Fact]
    public void Validador_cobra_coerencia_entre_tpRetPisCofins_e_vRetCSLL()
    {
        var validador = new DpsValidator();
        var ok = RequestComRetencoes();
        Assert.Empty(validador.Validar(ok));

        var semValor = ok with { Tributacao = ok.Tributacao with { RetencoesFederais = new RetencoesFederaisDps(null, 150m, null) } };
        Assert.Contains(validador.Validar(semValor), e => e.Contains("E0724"));

        var tipoZero = ok with { Tributacao = ok.Tributacao with { TpRetPisCofins = "0" } };
        Assert.Contains(validador.Validar(tipoZero), e => e.Contains("E0720"));

        var descontoDemais = ok with { Descontos = new DescontosDps(9_000m, 1_000m) };
        Assert.Contains(validador.Validar(descontoDemais), e => e.Contains("descontos"));
    }

    private static List<string> Filhos(XDocument doc, string pai) =>
        doc.Descendants(XName.Get(pai, Ns)).Single().Elements().Select(e => e.Name.LocalName).ToList();

    private static string Valor(XDocument doc, string elemento) => doc.Descendants(XName.Get(elemento, Ns)).Single().Value;
}
