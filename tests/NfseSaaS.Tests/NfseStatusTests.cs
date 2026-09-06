using NfseSaaS.Domain.Enums;
using Xunit;

namespace NfseSaaS.Tests;

public class NfseStatusTests
{
    [Fact]
    public void NfseStatus_Rascunho_deve_ser_o_valor_inicial_padrao()
    {
        var nfse = new NfseSaaS.Domain.Entities.Nfse();

        Assert.Equal(NfseStatus.Rascunho, nfse.Status);
    }

    [Theory]
    [InlineData(NfseStatus.Rascunho)]
    [InlineData(NfseStatus.Processando)]
    [InlineData(NfseStatus.Autorizada)]
    [InlineData(NfseStatus.Rejeitada)]
    [InlineData(NfseStatus.Cancelada)]
    [InlineData(NfseStatus.Substituida)]
    public void NfseStatus_deve_conter_todos_os_valores_esperados(NfseStatus status)
    {
        Assert.True(Enum.IsDefined(status));
    }
}
