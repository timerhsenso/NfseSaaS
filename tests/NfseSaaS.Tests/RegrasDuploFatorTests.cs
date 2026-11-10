using NfseSaaS.Application.UseCases.Seguranca;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>B3: textos e normalização do código da verificação em duas etapas.</summary>
public class RegrasDuploFatorTests
{
    [Theory]
    [InlineData("123456", "123456")]
    [InlineData(" 123 456 ", "123456")]
    [InlineData("123-456", "123456")]
    [InlineData(null, "")]
    public void Codigo_colado_do_email_vira_so_digitos(string? digitado, string esperado) =>
        Assert.Equal(esperado, RegrasDuploFator.NormalizarCodigo(digitado));

    [Fact]
    public void Email_do_codigo_traz_o_codigo_e_a_finalidade()
    {
        var login = RegrasDuploFator.Corpo("654321", ativacao: false);
        Assert.Contains("654321", login);
        Assert.Contains("para <strong>entrar</strong>", login);

        var ativacao = RegrasDuploFator.Corpo("654321", ativacao: true);
        Assert.Contains("ligar a verificação em duas etapas", ativacao);
        Assert.StartsWith("654321", RegrasDuploFator.AssuntoLogin("654321"));
    }

    [Fact]
    public void Aviso_diz_se_ligou_ou_desligou()
    {
        Assert.Contains("foi ligada", RegrasDuploFator.CorpoAviso(ligou: true));
        Assert.Contains("foi desligada", RegrasDuploFator.CorpoAviso(ligou: false));
    }
}
