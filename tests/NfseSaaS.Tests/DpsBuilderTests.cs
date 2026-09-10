using NfseSaaS.Nacional.Builders;
using NfseSaaS.Tests.TestData;
using Xunit;

namespace NfseSaaS.Tests;

public class DpsBuilderTests
{
    [Fact]
    public void DpsBuilder_deve_poder_ser_instanciado()
    {
        var builder = new DpsBuilder();

        Assert.NotNull(builder);
    }

    [Fact]
    public void Construir_deve_gerar_xml_contendo_o_Id_do_infDPS_retornado()
    {
        var builder = new DpsBuilder();
        var request = DpsRequestFactory.CriarRequestDeTeste();

        var (xml, infDpsId) = builder.Construir(request);

        Assert.StartsWith("DPS", infDpsId);
        Assert.Contains($"Id=\"{infDpsId}\"", xml);
    }

    [Fact]
    public void Construir_deve_incluir_os_dados_de_prestador_e_tomador_informados()
    {
        var builder = new DpsBuilder();
        var request = DpsRequestFactory.CriarRequestDeTeste();

        var (xml, _) = builder.Construir(request);

        Assert.Contains(request.Prestador.Cnpj, xml);
        Assert.Contains(request.Tomador.CnpjOuCpf, xml);
        Assert.Contains(request.Tomador.Nome, xml);
    }

    /// <summary>
    /// tpAmb agora vem pronto de fora (request.TpAmb, decidido por
    /// Empresa.TipoAmbiente em EmitirNfseUseCase) — o DpsBuilder só
    /// escreve o que recebeu, não decide mais nada a partir de
    /// appsettings/NfseNacionalOptions. Ver comentário de
    /// DpsRequest.TpAmb sobre por que essa tradução não pode acontecer
    /// dentro do módulo Nacional.
    /// </summary>
    [Theory]
    [InlineData("1", "<tpAmb>1</tpAmb>")]
    [InlineData("2", "<tpAmb>2</tpAmb>")]
    public void Construir_deve_escrever_o_tpAmb_recebido_no_request(string tpAmb, string tpAmbEsperado)
    {
        var builder = new DpsBuilder();
        var request = DpsRequestFactory.CriarRequestDeTeste() with { TpAmb = tpAmb };

        var (xml, _) = builder.Construir(request);

        Assert.Contains(tpAmbEsperado, xml);
    }

    [Theory]
    [InlineData("3")]
    [InlineData("Homologacao")]
    [InlineData("")]
    public void Construir_deve_rejeitar_tpAmb_fora_de_1_ou_2(string tpAmbInvalido)
    {
        var builder = new DpsBuilder();
        var request = DpsRequestFactory.CriarRequestDeTeste() with { TpAmb = tpAmbInvalido };

        Assert.Throws<ArgumentException>(() => builder.Construir(request));
    }
}
