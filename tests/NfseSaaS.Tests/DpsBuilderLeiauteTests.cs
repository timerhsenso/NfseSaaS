using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using NfseSaaS.Nacional.Options;
using NfseSaaS.Tests.TestData;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>
/// Regressão do E1235 de 03/11/2026 ("infDPS has invalid child element
/// 'cLocEmi'... expected: 'cMotivoEmisTI, finNFSe'"): a Produção Restrita
/// passou a exigir o leiaute da NT 009 enquanto a Produção continua no da
/// NT 004. O leiaute é escolhido por ambiente (NfseNacionalOptions).
///
/// A validação contra XSD usa os esquemas OFICIAIS de Produção Restrita de
/// 20/10/2026 (Schemas/ProducaoRestrita_20261020), copiados sem alteração
/// do pacote "esquemas_prod_restrita_20261020.zip" do portal gov.br/nfse.
/// Quando a SEFIN publicar esquema novo, adicione uma pasta nova em
/// Schemas/ em vez de sobrescrever esta.
/// </summary>
public class DpsBuilderLeiauteTests
{
    private const string Ns = "http://www.sped.fazenda.gov.br/nfse";

    private static readonly string PastaXsdProducaoRestrita =
        Path.Combine(AppContext.BaseDirectory, "Schemas", "ProducaoRestrita_20261020");

    [Fact]
    public void Defaults_devem_ser_Nt009_em_homologacao_e_Nt004_em_producao()
    {
        var options = new NfseNacionalOptions();

        Assert.Equal(LeiauteDps.Nt009, options.ObterLeiauteDps("2"));
        Assert.Equal(LeiauteDps.Nt004, options.ObterLeiauteDps("1"));
    }

    [Fact]
    public void Nt009_deve_gerar_DPS_valida_contra_o_XSD_oficial_de_producao_restrita()
    {
        var builder = DpsBuilderFactory.Criar(LeiauteDps.Nt009);
        var request = DpsRequestFactory.CriarRequestDeTeste() with { TpAmb = "2" };

        var (xml, _) = builder.Construir(request);

        var erros = ValidarContraXsd(xml, PastaXsdProducaoRestrita);
        Assert.True(erros.Count == 0, "DPS inválida no XSD de Produção Restrita:\n" + string.Join("\n", erros));
    }

    [Fact]
    public void Nt009_deve_escrever_finNFSe_regular_entre_tpEmit_e_cLocEmi()
    {
        var builder = DpsBuilderFactory.Criar(LeiauteDps.Nt009);

        var (xml, _) = builder.Construir(DpsRequestFactory.CriarRequestDeTeste());

        var filhos = NomesDosFilhos(xml, "infDPS");
        var iTpEmit = filhos.IndexOf("tpEmit");
        Assert.Equal("finNFSe", filhos[iTpEmit + 1]);
        Assert.Equal("cLocEmi", filhos[iTpEmit + 2]);
        Assert.Contains("<finNFSe>0</finNFSe>", xml);
    }

    [Fact]
    public void Nt009_deve_escrever_xDescServ_como_ultimo_elemento_do_cServ()
    {
        var builder = DpsBuilderFactory.Criar(LeiauteDps.Nt009);

        var (xml, _) = builder.Construir(DpsRequestFactory.CriarRequestDeTeste());

        Assert.Equal(new[] { "cTribNac", "cNBS", "xDescServ" }, NomesDosFilhos(xml, "cServ"));
    }

    [Fact]
    public void Nt004_deve_manter_o_leiaute_atual_de_producao()
    {
        var builder = DpsBuilderFactory.Criar(LeiauteDps.Nt004);

        var (xml, _) = builder.Construir(DpsRequestFactory.CriarRequestDeTeste());

        Assert.DoesNotContain("finNFSe", xml);
        var filhos = NomesDosFilhos(xml, "infDPS");
        Assert.Equal("cLocEmi", filhos[filhos.IndexOf("tpEmit") + 1]);
        Assert.Equal(new[] { "cTribNac", "xDescServ", "cNBS" }, NomesDosFilhos(xml, "cServ"));
    }

    [Theory]
    [InlineData("2", true)]  // Homologação → Nt009 (default)
    [InlineData("1", false)] // Produção → Nt004 (default, até 01/12/2026)
    public void Com_options_padrao_o_leiaute_deve_seguir_o_tpAmb_do_request(string tpAmb, bool esperaFinNfse)
    {
        var builder = DpsBuilderFactory.Criar();
        var request = DpsRequestFactory.CriarRequestDeTeste() with { TpAmb = tpAmb };

        var (xml, _) = builder.Construir(request);

        Assert.Equal(esperaFinNfse, xml.Contains("<finNFSe>"));
    }

    private static List<string> NomesDosFilhos(string xml, string elementoPai) =>
        XDocument.Parse(xml)
            .Descendants(XName.Get(elementoPai, Ns))
            .Single()
            .Elements()
            .Select(e => e.Name.LocalName)
            .ToList();

    private static List<string> ValidarContraXsd(string xml, string pastaXsd)
    {
        var erros = new List<string>();

        // xmldsig-core-schema.xsd (W3C) declara um DOCTYPE — sem
        // DtdProcessing.Parse o XmlSchemaSet recusa carregá-lo. É arquivo
        // local, versionado no repo; nada é buscado na rede (XmlResolver
        // restrito ao disco).
        var leituraXsd = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = new XmlUrlResolver()
        };

        var schemas = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
        using (var reader = XmlReader.Create(Path.Combine(pastaXsd, "DPS_v1.01.xsd"), leituraXsd))
            schemas.Add(Ns, reader);
        schemas.Compile();

        var leituraXml = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = schemas
        };
        leituraXml.ValidationEventHandler += (_, e) =>
            erros.Add($"[{e.Severity}] linha {e.Exception?.LineNumber}: {e.Message}");

        using var xmlReader = XmlReader.Create(new StringReader(xml), leituraXml);
        while (xmlReader.Read()) { }

        return erros;
    }
}
