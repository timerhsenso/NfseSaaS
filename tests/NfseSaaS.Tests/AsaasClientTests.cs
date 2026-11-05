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

    [Fact]
    public async Task Proxima_fatura_em_aberto_ignora_a_fatura_de_origem_e_pega_o_menor_vencimento()
    {
        var servidor = new ServidorFalso
        {
            Responder = _ => Json("""
                {"data":[
                  {"id":"pay_3","status":"PENDING","dueDate":"2027-01-10","invoiceUrl":"https://asaas/i/3"},
                  {"id":"pay_1","status":"PENDING","dueDate":"2026-11-10","invoiceUrl":"https://asaas/i/1"},
                  {"id":"pay_2","status":"PENDING","dueDate":"2026-12-10","invoiceUrl":"https://asaas/i/2"},
                  {"id":"pay_0","status":"OVERDUE","dueDate":"2026-10-10","invoiceUrl":"https://asaas/i/0"}
                ]}
                """)
        };

        var fatura = await Cliente(servidor).ObterProximaFaturaEmAbertoAsync("sub_1", "pay_1", CancellationToken.None);

        Assert.NotNull(fatura);
        Assert.Equal("pay_2", fatura!.Id);
        Assert.Equal(new DateOnly(2026, 12, 10), fatura.Vencimento);
        Assert.Equal("https://asaas/i/2", fatura.Url);
        Assert.Equal("https://api-sandbox.asaas.com/v3/subscriptions/sub_1/payments?status=PENDING", servidor.Recebidas.Single().Requisicao.RequestUri!.ToString());
    }

    [Fact]
    public async Task Sem_fatura_em_aberto_devolve_nulo()
    {
        var servidor = new ServidorFalso { Responder = _ => Json("""{"data":[]}""") };

        Assert.Null(await Cliente(servidor).ObterProximaFaturaEmAbertoAsync("sub_1", null, CancellationToken.None));
    }

    [Fact]
    public async Task Remove_fatura_e_adia_vencimento_com_os_verbos_certos()
    {
        var servidor = new ServidorFalso
        {
            Responder = r => r.Method == HttpMethod.Get
                ? Json("""{"id":"sub_1","nextDueDate":"2026-12-10","status":"ACTIVE"}""")
                : Json("""{"deleted":true,"id":"pay_2"}""")
        };
        var cliente = Cliente(servidor);

        await cliente.RemoverFaturaAsync("pay_2", CancellationToken.None);
        var proximo = await cliente.ObterProximoVencimentoAsync("sub_1", CancellationToken.None);
        await cliente.AlterarProximoVencimentoAsync("sub_1", new DateOnly(2027, 1, 10), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 12, 10), proximo);
        Assert.Equal(HttpMethod.Delete, servidor.Recebidas[0].Requisicao.Method);
        Assert.Equal("https://api-sandbox.asaas.com/v3/payments/pay_2", servidor.Recebidas[0].Requisicao.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Put, servidor.Recebidas[2].Requisicao.Method);
        Assert.Equal("https://api-sandbox.asaas.com/v3/subscriptions/sub_1", servidor.Recebidas[2].Requisicao.RequestUri!.ToString());
        Assert.Equal("""{"nextDueDate":"2027-01-10"}""", servidor.Recebidas[2].Corpo);
    }
}
