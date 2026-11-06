using NfseSaaS.Application.Common;
using Xunit;

namespace NfseSaaS.Tests;

public class SuporteOptionsTests
{
    [Theory]
    [InlineData("5571999998888", "(71) 99999-8888", "https://wa.me/5571999998888")]
    [InlineData("+55 (71) 3333-4444", "(71) 3333-4444", "https://wa.me/557133334444")]
    [InlineData("71999998888", "(71) 99999-8888", "https://wa.me/71999998888")]
    [InlineData("351912345678", "+351912345678", "https://wa.me/351912345678")]
    public void WhatsApp_formata_e_monta_o_link(string numero, string formatado, string link)
    {
        var s = new SuporteOptions { WhatsApp = numero };
        Assert.True(s.TemWhatsApp);
        Assert.Equal(formatado, s.WhatsAppFormatado);
        Assert.Equal(link, s.WhatsAppLink);
    }

    [Fact]
    public void Sem_contato_nao_mostra_nada()
    {
        var s = new SuporteOptions { Email = " ", WhatsApp = "123" };
        Assert.False(s.TemContato);
        Assert.Null(s.WhatsAppLink);
        Assert.Equal(string.Empty, s.TextoContato);
    }

    [Fact]
    public void Texto_do_contato_junta_email_e_whatsapp()
    {
        Assert.Equal("suporte@x.com", new SuporteOptions { Email = "suporte@x.com" }.TextoContato);
        Assert.Equal("suporte@x.com ou WhatsApp (71) 99999-8888",
            new SuporteOptions { Email = " suporte@x.com ", WhatsApp = "5571999998888" }.TextoContato);
    }
}
