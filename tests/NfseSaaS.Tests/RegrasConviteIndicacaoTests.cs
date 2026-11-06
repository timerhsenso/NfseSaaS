using NfseSaaS.Application.UseCases.Indicacao;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.Tests;

public class RegrasConviteIndicacaoTests
{
    [Fact]
    public void Separa_por_virgula_espaco_e_linha_normaliza_e_tira_repetidos()
    {
        var (validos, invalidos) = RegrasConviteIndicacao.Separar(" Ana@Empresa.com.br, joao@x.com;\nana@empresa.com.br  <maria@y.io>\r\nsem-arroba, a@b ");

        Assert.Equal(new[] { "ana@empresa.com.br", "joao@x.com", "maria@y.io" }, validos);
        Assert.Equal(new[] { "sem-arroba", "a@b" }, invalidos);
    }

    [Theory]
    [InlineData("fulano@empresa.com.br", true)]
    [InlineData("fulano.silva+nfse@sub.dominio.io", true)]
    [InlineData("fulano@empresa", false)]
    [InlineData("<script>@x.com", false)]
    [InlineData("a@b.c", false)]
    public void Valida_o_formato_do_email(string email, bool valido)
    {
        Assert.Equal(valido, RegrasConviteIndicacao.EmailValido(email));
    }

    [Fact]
    public void Texto_vazio_nao_tem_nada()
    {
        var (validos, invalidos) = RegrasConviteIndicacao.Separar("  \n ");
        Assert.Empty(validos);
        Assert.Empty(invalidos);
    }

    [Fact]
    public void Link_do_convite_leva_a_origem()
    {
        Assert.Equal("https://app/?ref=K7M2QX9A&origem=convite-email", RegrasConviteIndicacao.Link("https://app/?ref=K7M2QX9A"));
    }

    [Fact]
    public void Mensagem_codifica_nome_e_link_e_explica_o_reply_to()
    {
        var (assunto, corpo) = MensagemConviteIndicacao.Montar("Empresa <b>X</b>", "dono@x.com", "https://app/?ref=A&origem=convite-email");

        Assert.Equal("Empresa <b>X</b> indicou o micoempresa para você", assunto);
        Assert.Contains("Empresa &lt;b&gt;X&lt;/b&gt;", corpo);
        Assert.DoesNotContain("<b>X</b>", corpo);
        Assert.Contains("https://app/?ref=A&amp;origem=convite-email", corpo);
        Assert.Contains("vai direto para dono@x.com", corpo);
        Assert.Contains("Criar conta grátis", corpo);
    }
}
