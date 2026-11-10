using System.Xml.Linq;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Nacional.Options;
using NfseSaaS.Nacional.Models;
using NfseSaaS.Tests.TestData;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>C7: regras da substituição de NFS-e e o grupo subst na DPS (validado no XSD oficial).</summary>
public class SubstituicaoNfseTests
{
    private const string Chave = "29192072204747304000178000000000000126110000001234";

    private static ComparacaoSubstituicao Igual() => new(
        "52998224725", "52998224725",
        new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1),
        100m, 100m,
        "010501", "010501",
        "2919207", "2919207");

    [Fact]
    public void Simples_trava_cliente_competencia_e_valor()
    {
        Assert.Equal(
            new[] { CampoTravadoSubstituicao.Cliente, CampoTravadoSubstituicao.Competencia, CampoTravadoSubstituicao.Valor },
            RegrasSubstituicao.CamposTravados("3"));
        Assert.Equal(RegrasSubstituicao.CamposTravados("3"), RegrasSubstituicao.CamposTravados("2"));

        Assert.Empty(RegrasSubstituicao.CamposAlteradosIndevidamente("3", Igual()));
        // Serviço e local podem mudar no Simples.
        Assert.Empty(RegrasSubstituicao.CamposAlteradosIndevidamente("3", Igual() with { CodigoTributacaoNovo = "010401", LocalNovo = "2927408" }));
        Assert.Equal(new[] { "valor do serviço" }, RegrasSubstituicao.CamposAlteradosIndevidamente("3", Igual() with { ValorNovo = 120m }));
        Assert.Equal(new[] { "cliente (tomador)", "competência" },
            RegrasSubstituicao.CamposAlteradosIndevidamente("3", Igual() with { DocumentoTomadorNovo = "11144477735", CompetenciaNova = new DateOnly(2026, 10, 1) }));
    }

    [Fact]
    public void Nao_optante_trava_competencia_servico_e_local()
    {
        Assert.Equal(
            new[] { CampoTravadoSubstituicao.Competencia, CampoTravadoSubstituicao.Servico, CampoTravadoSubstituicao.LocalPrestacao },
            RegrasSubstituicao.CamposTravados("1"));
        Assert.Empty(RegrasSubstituicao.CamposAlteradosIndevidamente("1", Igual() with { ValorNovo = 150m, DocumentoTomadorNovo = "11144477735" }));
        Assert.Equal(new[] { "serviço (código de tributação)", "local da prestação" },
            RegrasSubstituicao.CamposAlteradosIndevidamente("1", Igual() with { CodigoTributacaoNovo = "010401", LocalNovo = "2927408" }));
        Assert.Contains("não optante", RegrasSubstituicao.MensagemCamposTravados("1", new[] { "local da prestação" }));
    }

    [Fact]
    public void Motivo_oficial_e_descricao_de_15_a_255_obrigatoria_no_99()
    {
        Assert.NotNull(RegrasSubstituicao.ProblemaNoMotivo(null, null));
        Assert.NotNull(RegrasSubstituicao.ProblemaNoMotivo("06", null));
        Assert.Null(RegrasSubstituicao.ProblemaNoMotivo("05", null));
        Assert.Contains("Outros", RegrasSubstituicao.ProblemaNoMotivo("99", " "));
        Assert.NotNull(RegrasSubstituicao.ProblemaNoMotivo("99", "curto"));
        Assert.Null(RegrasSubstituicao.ProblemaNoMotivo("99", "Descrição do serviço corrigida"));
        Assert.NotNull(RegrasSubstituicao.ProblemaNoMotivo("01", new string('x', 256)));
        Assert.Equal(new[] { "99", "01", "02", "03", "04", "05" }, RegrasSubstituicao.Motivos.Select(m => m.Codigo));
    }

    [Fact]
    public void Dps_com_subst_entre_cLocEmi_e_prest_valida_no_XSD()
    {
        var builder = DpsBuilderFactory.Criar(LeiauteDps.Nt009);
        var request = DpsRequestFactory.CriarRequestDeTeste() with
        {
            TpAmb = "2",
            Substituicao = new SubstituicaoDps(Chave, "99", "Descrição do serviço corrigida")
        };

        var (xml, _) = builder.Construir(request);

        Assert.Empty(DpsXsdValidator.Validar(xml, DpsXsdValidator.PastaProducaoRestrita20261020));
        var infDps = XDocument.Parse(xml).Descendants().Single(e => e.Name.LocalName == "infDPS");
        var nomes = infDps.Elements().Select(e => e.Name.LocalName).ToList();
        Assert.Equal(nomes.IndexOf("cLocEmi") + 1, nomes.IndexOf("subst"));
        Assert.Equal(nomes.IndexOf("subst") + 1, nomes.IndexOf("prest"));
        var subst = infDps.Elements().Single(e => e.Name.LocalName == "subst");
        Assert.Equal(new[] { Chave, "99", "Descrição do serviço corrigida" }, subst.Elements().Select(e => e.Value));
    }

    [Fact]
    public void Sem_descricao_o_xMotivo_nao_vai_e_sem_substituicao_nao_ha_grupo()
    {
        var builder = DpsBuilderFactory.Criar(LeiauteDps.Nt009);
        var request = DpsRequestFactory.CriarRequestDeTeste() with { TpAmb = "2" };

        Assert.DoesNotContain("<subst>", builder.Construir(request).XmlDps);

        var (xml, _) = builder.Construir(request with { Substituicao = new SubstituicaoDps(Chave, "05", null) });
        Assert.Empty(DpsXsdValidator.Validar(xml, DpsXsdValidator.PastaProducaoRestrita20261020));
        Assert.DoesNotContain("xMotivo", xml);
    }
}
