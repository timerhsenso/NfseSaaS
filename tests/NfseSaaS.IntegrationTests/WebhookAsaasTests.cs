using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// Webhook do Asaas com o banco real: token obrigatório, evento aplicado uma
/// vez só (idempotência pelo id) e as transições de situação da assinatura.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class WebhookAsaasTests
{
    private readonly AplicacaoTesteFactory _factory;

    public WebhookAsaasTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Testes_nunca_falam_com_o_Asaas_de_verdade()
    {
        // Mesmo com a chave do sandbox nos user-secrets do PC (AplicacaoTesteFactory).
        using var escopo = _factory.Services.CreateScope();
        Assert.False(escopo.ServiceProvider.GetRequiredService<IGatewayCobranca>().Configurado);
    }

    [Fact]
    public async Task Sem_token_configurado_o_webhook_recusa_tudo()
    {
        var resposta = await _factory.CreateClient().PostAsync("/api/webhooks/asaas",
            new StringContent("""{"id":"evt_x","event":"PAYMENT_RECEIVED"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Pagamento_ativa_evento_repetido_nao_reaplica_e_vencimento_marca_atraso()
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
        var processar = escopo.ServiceProvider.GetRequiredService<IProcessarWebhookAsaasUseCase>();

        var sufixo = Guid.NewGuid().ToString("N");
        var tenant = new Tenant { Documento = sufixo[..14], RazaoSocial = "Teste webhook " + sufixo };
        db.Tenants.Add(tenant);
        var assinatura = new Assinatura { TenantId = tenant.Id, AsaasAssinaturaId = "sub_" + sufixo, AsaasClienteId = "cus_" + sufixo };
        db.Assinaturas.Add(assinatura);
        await db.SaveChangesAsync();

        try
        {
            string Evento(string id, string tipo, string dueDate) => $$$"""
                {"id":"{{{id}}}","event":"{{{tipo}}}","dateCreated":"2026-11-10 10:00:00",
                 "payment":{"object":"payment","id":"pay_{{{sufixo}}}","subscription":"sub_{{{sufixo}}}","status":"RECEIVED","dueDate":"{{{dueDate}}}","invoiceUrl":"https://asaas/i/{{{sufixo}}}","campoNovo":123}}
                """;

            Assert.Equal("assinatura ativa", await processar.ExecutarAsync(Evento("evt_a_" + sufixo, "PAYMENT_RECEIVED", "2026-11-10"), CancellationToken.None));
            Assert.Equal("repetido", await processar.ExecutarAsync(Evento("evt_a_" + sufixo, "PAYMENT_RECEIVED", "2026-11-10"), CancellationToken.None));

            await db.Entry(assinatura).ReloadAsync();
            Assert.Equal(StatusAssinatura.Ativa, assinatura.Status);
            Assert.NotNull(assinatura.UltimoPagamentoEm);

            Assert.Equal("fatura vencida", await processar.ExecutarAsync(Evento("evt_b_" + sufixo, "PAYMENT_OVERDUE", "2026-12-10"), CancellationToken.None));
            await db.Entry(assinatura).ReloadAsync();
            Assert.Equal(StatusAssinatura.Atrasada, assinatura.Status);
            Assert.Equal(new DateOnly(2026, 12, 10), assinatura.AtrasadaDesde);

            Assert.Equal("ignorado: assinatura não encontrada",
                await processar.ExecutarAsync(Evento("evt_c_" + sufixo, "PAYMENT_RECEIVED", "2026-12-10").Replace("sub_" + sufixo, "sub_outra"), CancellationToken.None));

            Assert.Equal(3, await db.EventosCobranca.CountAsync(e => e.EventoId.EndsWith(sufixo)));
        }
        finally
        {
            await db.EventosCobranca.Where(e => e.EventoId.EndsWith(sufixo)).ExecuteDeleteAsync();
            await db.Assinaturas.Where(a => a.Id == assinatura.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(t => t.Id == tenant.Id).ExecuteDeleteAsync();
        }
    }
}
