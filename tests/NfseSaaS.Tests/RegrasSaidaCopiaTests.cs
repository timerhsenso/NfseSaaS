using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Application.UseCases.EmailNota;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>RC1: regras de quem recebe a nota como cópia e pede para sair.</summary>
public class RegrasSaidaCopiaTests
{
    [Theory]
    [InlineData("  Financeiro@Empresa.COM.br ", "financeiro@empresa.com.br")]
    [InlineData("a@b.co", "a@b.co")]
    [InlineData("sem-arroba", null)]
    [InlineData("dois espacos@x.com", null)]
    [InlineData("Nome <a@b.com>", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normaliza_o_email(string? digitado, string? esperado)
    {
        Assert.Equal(esperado, RegrasSaidaCopia.Normalizar(digitado));
    }

    [Fact]
    public void Email_invalido_vira_erro_de_regra()
    {
        Assert.Throws<RegraNegocioException>(() => RegrasSaidaCopia.NormalizarOuErro("x"));
    }

    [Fact]
    public void Principal_e_copia_sem_diferenciar_maiusculas()
    {
        Assert.True(RegrasSaidaCopia.EhPrincipal(" Tomador@Teste.com ", "tomador@teste.com"));
        Assert.False(RegrasSaidaCopia.EhPrincipal(null, "tomador@teste.com"));
        Assert.True(RegrasSaidaCopia.EhCopia("financeiro@teste.com;Contador@Teste.com", "contador@teste.com"));
        Assert.False(RegrasSaidaCopia.EhCopia("financeiro@teste.com", "contador@teste.com"));
        Assert.False(RegrasSaidaCopia.EhCopia(null, "contador@teste.com"));
    }

    [Fact]
    public void Remover_tira_so_o_endereco_pedido()
    {
        Assert.Equal("financeiro@teste.com;outro@teste.com",
            RegrasSaidaCopia.Remover("financeiro@teste.com;Contador@teste.com;outro@teste.com", "contador@teste.com"));
        Assert.Null(RegrasSaidaCopia.Remover("contador@teste.com", "contador@teste.com"));
        Assert.Equal("financeiro@teste.com", RegrasSaidaCopia.Remover("financeiro@teste.com", "nao@esta.com"));
    }

    [Theory]
    [InlineData("fulano@empresa.com.br", "fu***@empresa.com.br")]
    [InlineData("ab@x.com", "a***@x.com")]
    [InlineData("a@x.com", "a***@x.com")]
    [InlineData("sem-arroba", "***")]
    public void Mascara_o_email(string email, string esperado)
    {
        Assert.Equal(esperado, RegrasSaidaCopia.Mascarar(email));
    }

    [Fact]
    public void Codigo_de_confirmacao_e_aleatorio_e_so_o_hash_fica()
    {
        var (codigo1, hash1) = RegrasSaidaCopia.NovoCodigo();
        var (codigo2, _) = RegrasSaidaCopia.NovoCodigo();
        Assert.NotEqual(codigo1, codigo2);
        Assert.Equal(43, codigo1.Length); // 32 bytes em base64url, sem "="
        Assert.DoesNotContain('+', codigo1);
        Assert.DoesNotContain('/', codigo1);
        Assert.Equal(64, hash1.Length);
        Assert.Equal(hash1, RegrasSaidaCopia.Hash(codigo1));
        Assert.NotEqual(codigo1, hash1);
    }

    [Fact]
    public void Rodape_tem_o_link_de_saida_so_quando_informado()
    {
        var d = new DadosEmailNota(
            "Padaria Sol", "LFMS Consultoria", "LFMS - CONSULTORIA LTDA", "contato@lfms.com.br",
            "58", new DateOnly(2026, 11, 1), 344.21m, new DateTimeOffset(2026, 11, 12, 15, 0, 0, TimeSpan.Zero),
            "Manutenção", "2026/0001", "NFS123", false);
        var com = RegrasModeloEmailNota.MontarCorpoHtml("Oi", d, false, "https://x.test", "https://x.test/copia/sair?t=abc&x=1");
        Assert.Contains("Pedir para sair das cópias", com);
        Assert.Contains("https://x.test/copia/sair?t=abc&amp;x=1", com);
        Assert.True(com.IndexOf("Pedir para sair", StringComparison.Ordinal) < com.IndexOf("Enviado pela plataforma", StringComparison.Ordinal));

        var sem = RegrasModeloEmailNota.MontarCorpoHtml("Oi", d, false, "https://x.test");
        Assert.DoesNotContain("sair das cópias", sem);
    }
}
