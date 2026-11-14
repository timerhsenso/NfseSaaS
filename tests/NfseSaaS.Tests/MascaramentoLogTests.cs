using NfseSaaS.Application.Common;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>SEG1 — segredos e CPF/CNPJ de terceiros não vão para o log.</summary>
public class MascaramentoLogTests
{
    [Theory]
    [InlineData("Senha")]
    [InlineData("SenhaCertificado")]
    [InlineData("password")]
    [InlineData("AccessToken")]
    [InlineData("access_token")]
    [InlineData("ApiKey")]
    [InlineData("WebhookToken")]
    [InlineData("Authorization")]
    [InlineData("Cookie")]
    public void Nome_sensivel(string nome) => Assert.True(MascaramentoLog.NomeSensivel(nome));

    [Theory]
    [InlineData("EmpresaId")]
    [InlineData("TenantId")]
    [InlineData("Status")]
    [InlineData("Corpo")]
    [InlineData("EventoId")]
    [InlineData("Cnpj")]
    public void Nome_comum_continua(string nome) => Assert.False(MascaramentoLog.NomeSensivel(nome));

    [Fact]
    public void Corpo_json_de_terceiro_perde_segredos_e_cpf_cnpj()
    {
        var corpo = """{"errors":[{"code":"invalid"}],"customer":{"name":"Fulano","cpfCnpj":"12345678909","email":"f@x.com"},"access_token":"abc.def","apiKey":"$aact_123"}""";
        var mascarado = MascaramentoLog.MascararTexto(corpo);

        Assert.DoesNotContain("12345678909", mascarado);
        Assert.DoesNotContain("abc.def", mascarado);
        Assert.DoesNotContain("$aact_123", mascarado);
        Assert.Contains("\"cpfCnpj\":\"***\"", mascarado);
        Assert.Contains("Fulano", mascarado); // o resto continua legível
        Assert.Contains("invalid", mascarado);
    }

    [Fact]
    public void Links_assinados_formularios_e_bearer()
    {
        Assert.Equal("https://x/recebimento/confirmar?t=***", MascaramentoLog.MascararTexto("https://x/recebimento/confirmar?t=CfDJ8Abc-_123"));
        Assert.Equal("/Account/RedefinirSenha?email=a%40b.com&token=***", MascaramentoLog.MascararTexto("/Account/RedefinirSenha?email=a%40b.com&token=Q2ZESjh"));
        Assert.Equal("senha=*** e mais", MascaramentoLog.MascararTexto("senha=segredo123 e mais"));
        Assert.Equal("Authorization: Bearer ***", MascaramentoLog.MascararTexto("Authorization: Bearer eyJhbGciOi.xyz"));
    }

    [Fact]
    public void Texto_sem_segredo_nao_muda()
    {
        const string texto = "Webhook Asaas PAYMENT_CONFIRMED (evt_123) da assinatura sub_9: aplicado";
        Assert.Equal(texto, MascaramentoLog.MascararTexto(texto));
        Assert.Equal(string.Empty, MascaramentoLog.MascararTexto(null));
    }
}
