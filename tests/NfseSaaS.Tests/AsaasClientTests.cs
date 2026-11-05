using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Infrastructure.Cobranca;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>Cliente do Asaas contra um servidor falso: cabeçalhos, corpo e tratamento de erro.</summary>
public class AsaasClientTests
{
    private sealed class ServidorFalso : HttpMessageHandler
    {
        public List<(HttpRequestMessage Requisicao, string? Corpo)> Recebidas { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } = _ => new(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Recebidas.Add((request, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return Responder(request);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static AsaasClient Cliente(ServidorFalso servidor, string chave = "$aact_hmlg_teste") =>
        new(new HttpClient(servidor), Options.Create(new AsaasOptions { ApiKey = chave, BaseUrl = "https://api-sandbox.asaas.com/v3/" }), NullLogger<AsaasClient>.Instance);

    [Fact]
    public async Task Cria_assinatura_mensal_com_forma_de_pagamento_a_escolher()
    {
        var servidor = new ServidorFalso { Responder = _ => Json("""{"id":"sub_123","object":"subscription"}""") };

        var id = await Cliente(servidor).CriarAssinaturaMensalAsync("cus_1", 19m, new DateOnly(2026, 11, 10), "micoempresa - plano Mico", "tenant-1", CancellationToken.None);

        Assert.Equal("sub_123", id);
        var (requisicao, corpo) = Assert.Single(servidor.Recebidas);
        Assert.Equal(HttpMethod.Post, requisicao.Method);
        Assert.Equal("https://api-sandbox.asaas.com/v3/subscriptions", requisicao.RequestUri!.ToString());
        Assert.Equal("$aact_hmlg_teste", requisicao.Headers.GetValues("access_token").Single());
        Assert.Contains("micoempresa", requisicao.Headers.UserAgent.ToString());
        Assert.Contains("\"billingType\":\"UNDEFINED\"", corpo);
        Assert.Contains("\"cycle\":\"MONTHLY\"", corpo);
        Assert.Contains("\"nextDueDate\":\"2026-11-10\"", corpo);
        Assert.Contains("\"value\":19", corpo);
    }

    [Fact]
    public async Task Fatura_pendente_e_a_primeira_em_aberto()
    {
        var servidor = new ServidorFalso
        {
            Responder = _ => Json("""
                {"data":[
                  {"id":"pay_2","status":"PENDING","dueDate":"2026-12-10","invoiceUrl":"https://asaas/i/2"},
                  {"id":"pay_0","status":"RECEIVED","dueDate":"2026-10-10","invoiceUrl":"https://asaas/i/0"},
                  {"id":"pay_1","status":"OVERDUE","dueDate":"2026-11-10","invoiceUrl":"https://asaas/i/1"}
                ]}
                """)
        };

        Assert.Equal("https://asaas/i/1", await Cliente(servidor).ObterUrlFaturaPendenteAsync("sub_1", CancellationToken.None));
    }

    [Fact]
    public async Task Erro_do_Asaas_vira_mensagem_para_o_usuario()
    {
        var servidor = new ServidorFalso
        {
            Responder = _ => Json("""{"errors":[{"code":"invalid_cpfCnpj","description":"O CPF/CNPJ informado é inválido."}]}""", HttpStatusCode.BadRequest)
        };

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(
            () => Cliente(servidor).CriarClienteAsync("X", "123", null, "t", CancellationToken.None));
        Assert.Contains("CPF/CNPJ informado é inválido", erro.Message);
    }

    [Fact]
    public async Task Sem_chave_nao_chama_o_Asaas()
    {
        var servidor = new ServidorFalso();
        var cliente = Cliente(servidor, chave: "");

        Assert.False(cliente.Configurado);
        await Assert.ThrowsAsync<RegraNegocioException>(() => cliente.CancelarAssinaturaAsync("sub_1", CancellationToken.None));
        Assert.Empty(servidor.Recebidas);
    }
}
