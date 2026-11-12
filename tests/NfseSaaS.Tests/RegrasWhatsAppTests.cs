using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Conta;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>WhatsApp da conta: celular do Brasil guardado como 55+DDD+número.</summary>
public class RegrasWhatsAppTests
{
    [Theory]
    [InlineData("(71) 99999-8888", "5571999998888")]
    [InlineData("71999998888", "5571999998888")]
    [InlineData("+55 71 99999-8888", "5571999998888")]
    [InlineData("5571999998888", "5571999998888")]
    [InlineData("071 99999-8888", "5571999998888")]
    [InlineData("(11) 91234-5678", "5511912345678")]
    public void Normaliza_com_ou_sem_mascara_e_55(string entrada, string esperado)
    {
        Assert.Equal(esperado, RegrasWhatsApp.Normalizar(entrada));
    }

    [Theory]
    [InlineData("(71) 3333-4444")]     // fixo (sem o 9)
    [InlineData("(71) 89999-8888")]    // celular não começa com 8
    [InlineData("(20) 99999-8888")]    // DDD que não existe
    [InlineData("9999-8888")]          // sem DDD
    [InlineData("abc")]
    public void Recusa_o_que_nao_e_celular_do_brasil(string entrada)
    {
        Assert.Null(RegrasWhatsApp.Normalizar(entrada));
        Assert.Equal(RegrasWhatsApp.MensagemInvalido,
            Assert.Throws<RegraNegocioException>(() => RegrasWhatsApp.Validar(entrada)).Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Vazio_e_sem_whatsapp(string? entrada)
    {
        Assert.Null(RegrasWhatsApp.Validar(entrada));
    }

    [Fact]
    public void Formata_e_monta_o_link_da_conversa()
    {
        Assert.Equal("(71) 99999-8888", RegrasWhatsApp.Formatar("5571999998888"));
        Assert.Null(RegrasWhatsApp.Formatar(null));
        Assert.Equal("https://wa.me/5571999998888", RegrasWhatsApp.LinkConversa("5571999998888"));
        Assert.Equal("https://wa.me/5571999998888?text=Ol%C3%A1%2C%20tudo%20bem%3F",
            RegrasWhatsApp.LinkConversa("5571999998888", "Olá, tudo bem?"));
        Assert.Null(RegrasWhatsApp.LinkConversa(null));
    }
}
