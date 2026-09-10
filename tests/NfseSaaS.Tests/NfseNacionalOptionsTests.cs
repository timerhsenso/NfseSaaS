using NfseSaaS.Nacional.Options;
using Xunit;

namespace NfseSaaS.Tests;

public class NfseNacionalOptionsTests
{
    [Fact]
    public void NfseNacionalOptions_deve_ter_valores_padrao_seguros()
    {
        var options = new NfseNacionalOptions();

        Assert.Equal(string.Empty, options.BaseUrlHomologacao);
        Assert.Equal(string.Empty, options.BaseUrlProducao);
        Assert.Equal(60, options.TimeoutSeconds);
    }

    [Theory]
    [InlineData("1", "https://producao.exemplo/")]
    [InlineData("2", "https://homologacao.exemplo/")]
    public void ObterBaseUrl_deve_escolher_a_url_certa_pelo_tpAmb(string tpAmb, string urlEsperada)
    {
        var options = new NfseNacionalOptions
        {
            BaseUrlProducao = "https://producao.exemplo/",
            BaseUrlHomologacao = "https://homologacao.exemplo/"
        };

        Assert.Equal(urlEsperada, options.ObterBaseUrl(tpAmb));
    }
}
