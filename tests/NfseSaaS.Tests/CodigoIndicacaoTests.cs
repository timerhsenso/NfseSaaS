using NfseSaaS.Application.UseCases.Indicacao;
using Xunit;

namespace NfseSaaS.Tests;

public class CodigoIndicacaoTests
{
    [Fact]
    public void Codigo_gerado_tem_8_caracteres_validos_e_varia()
    {
        var codigos = Enumerable.Range(0, 200).Select(_ => CodigoIndicacao.Gerar()).ToList();

        Assert.All(codigos, c => Assert.Equal(c, CodigoIndicacao.Normalizar(c)));
        Assert.True(codigos.Distinct().Count() > 190);
    }

    [Theory]
    [InlineData("k7m2qx9a", "K7M2QX9A")]
    [InlineData(" K7M2QX9A ", "K7M2QX9A")]
    [InlineData("K7M2QX9", null)]          // curto
    [InlineData("K7M2QX9A0", null)]        // longo
    [InlineData("K7M2QX0A", null)]         // 0 fica de fora (confunde com O)
    [InlineData("<script>", null)]
    [InlineData(null, null)]
    public void Normalizar_aceita_so_o_formato_do_codigo(string? entrada, string? esperado)
    {
        Assert.Equal(esperado, CodigoIndicacao.Normalizar(entrada));
    }

    [Theory]
    [InlineData("email-nota", "email-nota")]
    [InlineData("Email-Nota", "email-nota")]
    [InlineData("a", null)]
    [InlineData("email nota", null)]
    [InlineData("<b>x</b>", null)]
    public void Origem_so_letras_numeros_e_hifen(string entrada, string? esperado)
    {
        Assert.Equal(esperado, CodigoIndicacao.NormalizarOrigem(entrada));
    }
}
