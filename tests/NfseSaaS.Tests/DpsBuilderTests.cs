using NfseSaaS.Nacional.Builders;
using NfseSaaS.Nacional.Models;
using Xunit;

namespace NfseSaaS.Tests;

public class DpsBuilderTests
{
    [Fact]
    public void DpsBuilder_deve_poder_ser_instanciado()
    {
        IDpsBuilder builder = new DpsBuilder();

        Assert.NotNull(builder);
    }

    [Fact]
    public void DpsBuilder_Construir_deve_lancar_NotImplemented_ate_a_migracao_da_POC_na_Fase_2()
    {
        IDpsBuilder builder = new DpsBuilder();
        var request = new DpsRequest(
            "04747304000178", "366293", "2919207",
            "13529565000102", "Cliente Teste",
            1001, "00001", DateOnly.FromDateTime(DateTime.Today),
            10.00m, "010701", "Teste");

        Assert.Throws<NotImplementedException>(() => builder.Construir(request));
    }
}
