using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Infrastructure.Email;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>
/// Envio da nota por e-mail no cadastro do cliente: desligado por padrão,
/// ligado exige e-mail, até 5 cópias válidas em Cc ou Cco.
/// </summary>
public class EnvioEmailClienteTests
{
    private static CadastrarClienteRequest Cadastro(string? email, bool enviar, IReadOnlyList<string>? copias = null, bool oculta = false) => new(
        Guid.NewGuid(), "11222333000181", "Cliente Teste", "Teste", email, null, "2927408", "40010000",
        "Rua A", "1", null, "Centro", "BA", false, enviar, copias, oculta);

    private static AtualizarClienteRequest Edicao(string? email, bool enviar, IReadOnlyList<string>? copias = null) => new(
        "Cliente Teste", "Teste", email, null, "2927408", "40010000", "Rua A", "1", null, "Centro", "BA", false, enviar, copias);

    private static List<string> Erros(CadastrarClienteRequest r) =>
        new CadastrarClienteRequestValidator().Validate(r).Errors.Select(e => e.ErrorMessage).ToList();

    [Fact]
    public void Padrao_do_cadastro_e_nao_enviar()
    {
        var r = new CadastrarClienteRequest(Guid.NewGuid(), "11222333000181", "N", "A", null, null, "2927408", "40010000", "R", "1", null, "B", "BA");
        Assert.False(r.EnviarNfsePorEmail);
        Assert.False(r.CopiaOculta);
        Assert.Null(r.EmailsCopia);
        Assert.Empty(Erros(r));
    }

    [Fact]
    public void Envio_ligado_sem_email_e_recusado_no_cadastro_e_na_edicao()
    {
        Assert.Contains(Erros(Cadastro(null, enviar: true)), m => m.Contains("Informe o e-mail do cliente"));
        Assert.Contains(Erros(Cadastro("  ", enviar: true)), m => m.Contains("Informe o e-mail do cliente"));

        var edicao = new AtualizarClienteRequestValidator().Validate(Edicao(null, enviar: true));
        Assert.Contains(edicao.Errors, e => e.ErrorMessage.Contains("Informe o e-mail do cliente"));

        Assert.Empty(Erros(Cadastro("tomador@empresa.com", enviar: true)));
        Assert.Empty(Erros(Cadastro(null, enviar: false)));
    }

    [Fact]
    public void Ate_cinco_copias_validas()
    {
        var cinco = Enumerable.Range(1, 5).Select(i => $"c{i}@x.com").ToList();
        Assert.Empty(Erros(Cadastro("t@x.com", true, cinco)));

        var seis = Enumerable.Range(1, 6).Select(i => $"c{i}@x.com").ToList();
        Assert.Contains(Erros(Cadastro("t@x.com", true, seis)), m => m.Contains("no máximo 5"));

        // Repetidos e linhas em branco não contam.
        var comRepetidos = cinco.Concat(new[] { "C1@X.COM", " ", "" }).ToList();
        Assert.Empty(Erros(Cadastro("t@x.com", true, comRepetidos)));
    }

    [Theory]
    [InlineData("sem-arroba.com")]
    [InlineData("dois@@x.com")]
    [InlineData("@x.com")]
    [InlineData("nome@")]
    [InlineData("com espaco@x.com")]
    public void Copia_invalida_e_recusada(string copia)
    {
        Assert.Contains(Erros(Cadastro("t@x.com", true, new[] { copia })), m => m.Contains("E-mail em cópia inválido"));
    }

    [Fact]
    public void Copia_igual_ao_principal_e_recusada_e_formato_vale_mesmo_desligado()
    {
        Assert.Contains(Erros(Cadastro("T@x.com", true, new[] { "t@X.com" })), m => m.Contains("e-mail principal"));
        Assert.Contains(Erros(Cadastro(null, false, new[] { "invalido" })), m => m.Contains("E-mail em cópia inválido"));
    }

    [Fact]
    public void Coluna_junta_e_separa_sem_vazios_nem_repetidos()
    {
        Assert.Null(EmailsCopiaCliente.Juntar(null));
        Assert.Null(EmailsCopiaCliente.Juntar(new[] { " ", "" }));
        Assert.Equal("a@x.com;b@y.com", EmailsCopiaCliente.Juntar(new[] { " a@x.com ", "b@y.com", "A@X.com" }));
        Assert.Equal(new[] { "a@x.com", "b@y.com" }, EmailsCopiaCliente.Separar("a@x.com;b@y.com;"));
        Assert.Empty(EmailsCopiaCliente.Separar(null));
        Assert.True(EmailsCopiaCliente.TamanhoMaximoColuna >= EmailsCopiaCliente.Maximo * EmailsCopiaCliente.TamanhoMaximoEndereco + EmailsCopiaCliente.Maximo - 1);
    }

    [Fact]
    public void Smtp_poe_copias_em_cc_ou_cco_e_ignora_endereco_invalido()
    {
        var cc = new MimeMessage();
        SmtpEmailSender.AdicionarCopias(cc, new MensagemEmail("t@x.com", "A", "B", Copias: new[] { "a@x.com", "invalido", "b@y.com" }), NullLogger.Instance);
        Assert.Equal(new[] { "a@x.com", "b@y.com" }, cc.Cc.Mailboxes.Select(m => m.Address));
        Assert.Empty(cc.Bcc);

        var cco = new MimeMessage();
        SmtpEmailSender.AdicionarCopias(cco, new MensagemEmail("t@x.com", "A", "B", Copias: new[] { "a@x.com" }, CopiasOcultas: true), NullLogger.Instance);
        Assert.Empty(cco.Cc);
        Assert.Equal(new[] { "a@x.com" }, cco.Bcc.Mailboxes.Select(m => m.Address));

        var sem = new MimeMessage();
        SmtpEmailSender.AdicionarCopias(sem, new MensagemEmail("t@x.com", "A", "B"), NullLogger.Instance);
        Assert.Empty(sem.Cc);
        Assert.Empty(sem.Bcc);
    }
}
