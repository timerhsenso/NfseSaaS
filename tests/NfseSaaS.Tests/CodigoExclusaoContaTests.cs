using NfseSaaS.Application.UseCases.Plataforma;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>Código de confirmação da exclusão de conta (sem banco nem e-mail).</summary>
public class CodigoExclusaoContaTests
{
    [Fact]
    public void Gera_seis_digitos_e_confere_so_o_codigo_certo()
    {
        for (var i = 0; i < 200; i++)
        {
            var codigo = CodigoExclusaoConta.Gerar();
            Assert.Matches("^[0-9]{6}$", codigo);

            var hash = CodigoExclusaoConta.Hash(codigo);
            Assert.True(CodigoExclusaoConta.Confere(hash, codigo));
            Assert.True(CodigoExclusaoConta.Confere(hash, $" {codigo} "));
            Assert.False(CodigoExclusaoConta.Confere(hash, codigo == "000000" ? "000001" : "000000"));
            Assert.False(CodigoExclusaoConta.Confere(hash, null));
            Assert.False(CodigoExclusaoConta.Confere(hash, ""));
        }
    }

    [Theory]
    [InlineData("carlos@micoempresa.com.br", "c****s@micoempresa.com.br")]
    [InlineData("ab@x.com", "a*@x.com")]
    [InlineData("a@x.com", "a*@x.com")]
    [InlineData("umnomemuitocomprido@x.com", "u******o@x.com")]
    [InlineData("semarroba", "***")]
    public void Mascara_o_email(string email, string esperado)
    {
        Assert.Equal(esperado, CodigoExclusaoConta.MascararEmail(email));
    }
}
