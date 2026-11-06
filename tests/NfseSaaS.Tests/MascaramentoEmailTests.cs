using System.Net;
using NfseSaaS.Application.Common;
using Xunit;

namespace NfseSaaS.Tests;

public class MascaramentoEmailTests
{
    // Token do Identity típico: base64 com caracteres que mudam na URL e no HTML.
    private const string Token = "CfDJ8+abc/xyz==&<1>";

    [Fact]
    public void Esconde_o_token_puro_na_url_e_no_html()
    {
        var corpo = $"""
            <a href="https://app/Account/RedefinirSenha?email=a%40b.com&token={Uri.EscapeDataString(Token)}">link</a>
            <a href="{WebUtility.HtmlEncode("https://app/x?t=" + Uri.EscapeDataString(Token))}">outro</a>
            <li>Token: {WebUtility.HtmlEncode(Token)}</li>
            texto {Token}
            """;

        var mascarado = MascaramentoEmail.Mascarar(corpo, new[] { Token });

        Assert.DoesNotContain(Uri.EscapeDataString(Token), mascarado);
        Assert.DoesNotContain(WebUtility.HtmlEncode(Token), mascarado);
        Assert.DoesNotContain(Token, mascarado);
        Assert.DoesNotContain("CfDJ8", mascarado);
        Assert.True(MascaramentoEmail.TemSegredoOculto(mascarado));
        Assert.Contains("email=a%40b.com", mascarado);
    }

    [Fact]
    public void Sem_segredo_o_corpo_fica_igual()
    {
        Assert.Equal("<p>oi</p>", MascaramentoEmail.Mascarar("<p>oi</p>", null));
        Assert.Equal("<p>oi</p>", MascaramentoEmail.Mascarar("<p>oi</p>", new[] { "", "nao-esta-no-corpo" }));
        Assert.False(MascaramentoEmail.TemSegredoOculto("<p>oi</p>"));
    }
}
