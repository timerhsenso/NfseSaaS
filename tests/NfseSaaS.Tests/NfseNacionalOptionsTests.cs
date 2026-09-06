using NfseSaaS.Nacional.Options;
using Xunit;

namespace NfseSaaS.Tests;

public class NfseNacionalOptionsTests
{
    [Fact]
    public void NfseNacionalOptions_deve_ter_valores_padrao_seguros()
    {
        var options = new NfseNacionalOptions();

        Assert.Equal("Homologacao", options.Ambiente);
        Assert.Equal(60, options.TimeoutSeconds);
        Assert.Equal(string.Empty, options.BaseUrl);
    }
}
