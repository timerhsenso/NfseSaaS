using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.EmailNota;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Email;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>Texto do e-mail da nota: campos, montagem, modelo em vigor e validação.</summary>
public sealed class RegrasModeloEmailNotaTests
{
    private static DadosEmailNota Nota(bool homologacao = false, string cliente = "Padaria Sol", string? chave = "NFS123") => new(
        cliente, "LFMS Consultoria", "LFMS - CONSULTORIA LTDA", "contato@lfms.com.br",
        "58", new DateOnly(2026, 11, 1), 344.21m, new DateTimeOffset(2026, 11, 12, 15, 0, 0, TimeSpan.Zero),
        "Manutenção do sistema\nmódulo RHU", "2026/0001", chave, homologacao);

    [Fact]
    public void Exemplo_do_Carlos_vira_texto_com_os_dados_da_nota()
    {
        var texto = RegrasModeloEmailNota.Preencher(
            "Você está recebendo a nota [Número da nota], referente ao mês [mês por extenso], pelo site micoempresa da empresa [Empresa]",
            RegrasModeloEmailNota.Valores(Nota()));

        Assert.Equal("Você está recebendo a nota 58, referente ao mês novembro de 2026, pelo site micoempresa da empresa LFMS Consultoria", texto);
    }

    [Fact]
    public void Campos_valores_e_desconhecidos()
    {
        var valores = RegrasModeloEmailNota.Valores(Nota());
        Assert.Equal("R$ 344,21", valores[RegrasModeloEmailNota.CampoValor].Replace(' ', ' '));
        Assert.Equal("11/2026", valores[RegrasModeloEmailNota.CampoCompetencia]);
        Assert.Equal("12/11/2026", valores[RegrasModeloEmailNota.CampoEmissao]);
        Assert.Equal("Manutenção do sistema módulo RHU", valores[RegrasModeloEmailNota.CampoServico]);
        Assert.Equal("2026/0001", valores[RegrasModeloEmailNota.CampoContrato]);

        // Colchete que não é campo fica como está, e a tela avisa.
        Assert.Equal("Olá [Fulano], nota 58", RegrasModeloEmailNota.Preencher("Olá [Fulano], nota [Número da nota]", valores));
        Assert.Equal(new[] { "[Fulano]" }, RegrasModeloEmailNota.CamposDesconhecidos("Olá [Fulano], [cliente]"));
    }

    [Fact]
    public void Assunto_em_uma_linha_com_TESTE_na_homologacao_e_padrao_se_vazio()
    {
        Assert.Equal("Nota 58 de novembro de 2026",
            RegrasModeloEmailNota.MontarAssunto("Nota [Número da nota]\nde [Mês por extenso]", Nota()));
        Assert.StartsWith("[TESTE] Nota fiscal 58", RegrasModeloEmailNota.MontarAssunto("   ", Nota(homologacao: true)));
    }

    [Fact]
    public void Corpo_escapa_o_texto_mantem_o_quadro_fixo_e_o_rodape_e_opcional()
    {
        var corpo = RegrasModeloEmailNota.MontarCorpoHtml(
            "Olá, [Cliente].\n\nLinha 1\nLinha 2 <script>x</script>", Nota(cliente: "Tom & <b>Cia</b>"), true, "https://x.test");

        Assert.Contains("Tom &amp; &lt;b&gt;Cia&lt;/b&gt;.</p>", corpo);
        Assert.StartsWith("<p>Olá, Tom", System.Net.WebUtility.HtmlDecode(corpo));
        Assert.Contains("Linha 1<br>Linha 2 &lt;script&gt;", corpo);
        Assert.DoesNotContain("<script>", corpo);
        Assert.Contains("ConsultaPublica", corpo);
        Assert.Contains("NFS123", corpo);
        Assert.Contains("Crie sua conta grátis", System.Net.WebUtility.HtmlDecode(corpo));

        var semRodape = RegrasModeloEmailNota.MontarCorpoHtml("Oi", Nota(homologacao: true, chave: null), false, "https://x.test");
        Assert.DoesNotContain("Crie sua conta", System.Net.WebUtility.HtmlDecode(semRodape));
        Assert.DoesNotContain("ConsultaPublica", semRodape);
        Assert.Contains("TESTES", semRodape);
    }

    [Fact]
    public void Modelo_em_vigor_empresa_depois_conta_depois_padrao()
    {
        var conta = new ModeloEmailNota { Assunto = "Conta", Mensagem = "M conta", ResponderPara = "conta@x.com", OcultarDivulgacao = true };
        var empresa = new ModeloEmailNota { EmpresaId = Guid.NewGuid(), Assunto = "Empresa", Mensagem = "M empresa", CopiaOcultaPara = "cco@x.com" };

        var deEmpresa = RegrasModeloEmailNota.Resolver(empresa, conta, podeOcultarDivulgacao: true);
        Assert.Equal("Empresa", deEmpresa.Assunto);
        Assert.Null(deEmpresa.ResponderPara);
        Assert.Equal("cco@x.com", deEmpresa.CopiaOcultaPara);
        Assert.True(deEmpresa.OcultarDivulgacao); // rodapé é da conta

        Assert.Equal("Conta", RegrasModeloEmailNota.Resolver(null, conta, true).Assunto);
        Assert.False(RegrasModeloEmailNota.Resolver(null, conta, podeOcultarDivulgacao: false).OcultarDivulgacao);
        Assert.Equal(RegrasModeloEmailNota.AssuntoPadrao, RegrasModeloEmailNota.Resolver(null, null, true).Assunto);
    }

    [Fact]
    public void Validacao_de_assunto_mensagem_e_emails()
    {
        Assert.Empty(RegrasModeloEmailNota.Validar("A", "B", "a@b.com", null));
        Assert.Equal(2, RegrasModeloEmailNota.Validar(" ", "", null, null).Count);
        Assert.Single(RegrasModeloEmailNota.Validar(new string('a', 151), "B", null, null));
        Assert.Single(RegrasModeloEmailNota.Validar("A", "B", "sem-arroba", null));
        Assert.Single(RegrasModeloEmailNota.Validar("A", "B", null, "a@b.com;c@d.com"));
    }

    [Fact]
    public void Copia_oculta_fixa_vai_em_Cco_sem_repetir_quem_ja_recebe()
    {
        var mensagem = new MimeMessage();
        SmtpEmailSender.AdicionarCopias(mensagem,
            new MensagemEmail("cliente@x.com", "A", "B", Copias: new[] { "fin@x.com" }, CopiaOcultaFixa: "escritorio@y.com"),
            NullLogger.Instance);
        Assert.Equal("fin@x.com", Assert.Single(mensagem.Cc.Mailboxes).Address);
        Assert.Equal("escritorio@y.com", Assert.Single(mensagem.Bcc.Mailboxes).Address);

        var repetida = new MimeMessage();
        SmtpEmailSender.AdicionarCopias(repetida, new MensagemEmail("cliente@x.com", "A", "B", CopiaOcultaFixa: "CLIENTE@x.com"), NullLogger.Instance);
        Assert.Empty(repetida.Bcc);
    }
}
