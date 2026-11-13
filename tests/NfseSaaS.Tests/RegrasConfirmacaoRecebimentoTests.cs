using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.EmailNota;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>CR1: campo [Confirmar recebimento] no e-mail da nota e regras da confirmação.</summary>
public class RegrasConfirmacaoRecebimentoTests
{
    private static DadosEmailNota Nota() => new(
        "Padaria Sol", "RHSENSO", "LFMS - CONSULTORIA LTDA", "contato@lfms.com.br",
        "58", new DateOnly(2026, 11, 1), 344.21m, new DateTimeOffset(2026, 11, 12, 15, 0, 0, TimeSpan.Zero),
        "Manutenção", "2026/0001", "NFS123", false);

    private const string Link = "https://x.test/recebimento/confirmar?t=abc&z=1";

    [Fact]
    public void Sozinho_na_linha_vira_botao()
    {
        var html = RegrasModeloEmailNota.MontarCorpoHtml("Oi.\n\n[Confirmar recebimento]\n\nAté.", Nota(), false, "https://x.test", linkConfirmacao: Link);
        Assert.Contains("background:#16a34a", html);
        Assert.Contains(">Confirmar recebimento</a>", html);
        Assert.Contains("https://x.test/recebimento/confirmar?t=abc&amp;z=1", html);
        Assert.DoesNotContain("[Confirmar recebimento]", html);
    }

    [Fact]
    public void Na_frase_vira_link_e_o_texto_fica_escapado()
    {
        var html = RegrasModeloEmailNota.MontarCorpoHtml("Clique aqui <e> confirme - [confirmar RECEBIMENTO] obrigado", Nota(), false, "https://x.test", linkConfirmacao: Link);
        Assert.Contains("Clique aqui &lt;e&gt; confirme - <a href=", html);
        Assert.Contains(">confirmar recebimento</a> obrigado", html);
        Assert.DoesNotContain("background:#16a34a", html);
    }

    [Fact]
    public void Sem_link_o_campo_some()
    {
        var html = RegrasModeloEmailNota.MontarCorpoHtml("Oi.\n[Confirmar recebimento]\nAté.", Nota(), false, "https://x.test");
        Assert.DoesNotContain("Confirmar recebimento", html);
        Assert.Contains("<p>Oi.<br>At&#233;.</p>", html);
    }

    [Fact]
    public void Pedido_de_confirmacao_e_assunto()
    {
        Assert.True(RegrasModeloEmailNota.PedeConfirmacao("ok [Confirmar Recebimento]"));
        Assert.False(RegrasModeloEmailNota.PedeConfirmacao("ok"));
        Assert.Contains(RegrasModeloEmailNota.CampoConfirmarRecebimento, RegrasModeloEmailNota.MensagemPadrao);
        Assert.Contains(RegrasModeloEmailNota.Campos, c => c.Etiqueta == RegrasModeloEmailNota.CampoConfirmarRecebimento);
        Assert.Empty(RegrasModeloEmailNota.CamposDesconhecidos("[Confirmar recebimento]"));
        // Assunto não tem link: recusado.
        Assert.NotEmpty(RegrasModeloEmailNota.Validar("Nota [Confirmar recebimento]", "Oi", null, null));
        Assert.Empty(RegrasModeloEmailNota.Validar("Nota", "Oi [Confirmar recebimento]", null, null));
    }

    [Theory]
    [InlineData("carlos@gmail.com", "ca***@gm***.com")]
    [InlineData("ana@empresa.com.br", "an***@em***.com.br")]
    [InlineData("a@b.co", "a***@b***.co")]
    [InlineData("xy@localhost", "x***@lo***")]
    [InlineData("sem-arroba", "***")]
    public void Mascara_nome_e_dominio(string email, string esperado)
    {
        Assert.Equal(esperado, RegrasConfirmacaoRecebimento.Mascarar(email));
    }

    [Fact]
    public void Destinatarios_de_todos_os_envios()
    {
        var d = RegrasConfirmacaoRecebimento.Destinatarios(new (string, string?)[]
        {
            ("Tomador@Teste.com", "financeiro@teste.com;contador@teste.com"),
            ("novo@teste.com", "tomador@teste.com") // virou cópia depois: continua principal
        });
        Assert.True(d["tomador@teste.com"]);
        Assert.True(d["novo@teste.com"]);
        Assert.False(d["financeiro@teste.com"]);
        Assert.False(d["CONTADOR@teste.com"]);
        Assert.Equal(4, d.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(365)]
    public void Validade_aceita(int dias) => Assert.Equal(dias, RegrasConfirmacaoRecebimento.ValidarDias(dias));

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public void Validade_recusada(int dias) =>
        Assert.Throws<RegraNegocioException>(() => RegrasConfirmacaoRecebimento.ValidarDias(dias));
}
