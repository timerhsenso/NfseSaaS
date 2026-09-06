using Microsoft.Extensions.Options;
using NfseSaaS.Nacional.Builders;
using NfseSaaS.Nacional.Options;
using NfseSaaS.Tests.TestData;
using Xunit;

namespace NfseSaaS.Tests;

public class DpsBuilderTests
{
    private static DpsBuilder CriarBuilder(string ambiente = "ProducaoRestrita")
    {
        var options = Options.Create(new NfseNacionalOptions { Ambiente = ambiente });
        return new DpsBuilder(options);
    }

    [Fact]
    public void DpsBuilder_deve_poder_ser_instanciado()
    {
        var builder = CriarBuilder();

        Assert.NotNull(builder);
    }

    [Fact]
    public void Construir_deve_gerar_xml_contendo_o_Id_do_infDPS_retornado()
    {
        var builder = CriarBuilder();
        var request = DpsRequestFactory.CriarRequestDeTeste();

        var (xml, infDpsId) = builder.Construir(request);

        Assert.StartsWith("DPS", infDpsId);
        Assert.Contains($"Id=\"{infDpsId}\"", xml);
    }

    [Fact]
    public void Construir_deve_incluir_os_dados_de_prestador_e_tomador_informados()
    {
        var builder = CriarBuilder();
        var request = DpsRequestFactory.CriarRequestDeTeste();

        var (xml, _) = builder.Construir(request);

        Assert.Contains(request.Prestador.Cnpj, xml);
        Assert.Contains(request.Tomador.CnpjOuCpf, xml);
        Assert.Contains(request.Tomador.Nome, xml);
    }

    [Theory]
    [InlineData("Producao", "<tpAmb>1</tpAmb>")]
    [InlineData("Homologacao", "<tpAmb>2</tpAmb>")]
    [InlineData("ProducaoRestrita", "<tpAmb>2</tpAmb>")]
    public void Construir_deve_mapear_o_ambiente_configurado_para_o_tpAmb_correto(string ambiente, string tpAmbEsperado)
    {
        var builder = CriarBuilder(ambiente);
        var request = DpsRequestFactory.CriarRequestDeTeste();

        var (xml, _) = builder.Construir(request);

        Assert.Contains(tpAmbEsperado, xml);
    }
}