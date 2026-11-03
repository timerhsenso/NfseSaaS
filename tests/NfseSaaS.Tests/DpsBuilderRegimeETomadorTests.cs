using System.Xml.Linq;
using NfseSaaS.Nacional.Models;
using NfseSaaS.Nacional.Options;
using NfseSaaS.Tests.TestData;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>
/// Combinações de tomador (CPF/CNPJ) e regime do prestador (Não optante,
/// MEI, ME/EPP) que antes geravam DPS inválida no schema (E1235) e só
/// apareciam na SEFIN. Todo teste valida contra o XSD oficial.
/// </summary>
public class DpsBuilderRegimeETomadorTests
{
    private const string Ns = "http://www.sped.fazenda.gov.br/nfse";

    // CPF sintaticamente válido (dígitos verificadores corretos), de teste.
    private const string CpfTeste = "52998224725";

    [Fact]
    public void Tomador_com_CPF_deve_gerar_elemento_CPF_e_DPS_valida()
    {
        var request = RequestBase() with { Tomador = RequestBase().Tomador with { CnpjOuCpf = CpfTeste } };

        var xml = ConstruirValidando(request);

        Assert.Equal("CPF", PrimeiroFilho(xml, "toma"));
        Assert.Contains($"<CPF>{CpfTeste}</CPF>", xml);
    }

    [Fact]
    public void Tomador_com_CNPJ_formatado_deve_sair_sem_mascara()
    {
        var request = RequestBase() with { Tomador = RequestBase().Tomador with { CnpjOuCpf = "13.529.565/0001-02" } };

        var xml = ConstruirValidando(request);

        Assert.Equal("CNPJ", PrimeiroFilho(xml, "toma"));
        Assert.Contains("<CNPJ>13529565000102</CNPJ>", xml);
    }

    [Fact]
    public void Prestador_nao_optante_deve_gerar_indTotTrib_e_omitir_regApTribSN()
    {
        var request = RequestBase() with
        {
            Prestador = RequestBase().Prestador with { OpSimpNac = "1", RegApTribSN = "1" },
            Tributacao = RequestBase().Tributacao with { PercentualTotalTributosSimplesNacional = "" }
        };

        var xml = ConstruirValidando(request);

        Assert.Contains("<indTotTrib>0</indTotTrib>", xml);
        Assert.DoesNotContain("pTotTribSN", xml);
        Assert.DoesNotContain("regApTribSN", xml);
    }

    [Fact]
    public void Prestador_MEI_deve_gerar_pTotTribSN_e_omitir_regApTribSN()
    {
        var request = RequestBase() with { Prestador = RequestBase().Prestador with { OpSimpNac = "2", RegApTribSN = "" } };

        var xml = ConstruirValidando(request);

        Assert.Contains("<pTotTribSN>3.00</pTotTribSN>", xml);
        Assert.DoesNotContain("regApTribSN", xml);
    }

    [Fact]
    public void Prestador_ME_EPP_deve_gerar_pTotTribSN_e_regApTribSN()
    {
        var xml = ConstruirValidando(RequestBase());

        Assert.Contains("<pTotTribSN>3.00</pTotTribSN>", xml);
        Assert.Contains("<regApTribSN>1</regApTribSN>", xml);
    }

    [Fact]
    public void Prestador_sem_IM_telefone_e_email_nao_deve_gerar_elementos_vazios()
    {
        var request = RequestBase() with
        {
            Prestador = RequestBase().Prestador with { InscricaoMunicipal = "", Telefone = " ", Email = "" }
        };

        var xml = ConstruirValidando(request);

        Assert.DoesNotContain("<IM", xml);
        Assert.DoesNotContain("<fone", xml);
        Assert.DoesNotContain("<email", xml);
    }

    private static DpsRequest RequestBase() => DpsRequestFactory.CriarRequestDeTeste() with { TpAmb = "2" };

    private static string ConstruirValidando(DpsRequest request)
    {
        var (xml, _) = DpsBuilderFactory.Criar(LeiauteDps.Nt009).Construir(request);

        var erros = DpsXsdValidator.Validar(xml, DpsXsdValidator.PastaProducaoRestrita20261020);
        Assert.True(erros.Count == 0, "DPS inválida no XSD:\n" + string.Join("\n", erros));

        return xml;
    }

    private static string PrimeiroFilho(string xml, string elementoPai) =>
        XDocument.Parse(xml).Descendants(XName.Get(elementoPai, Ns)).Single().Elements().First().Name.LocalName;
}
