using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Cobranca;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// L5 com o banco real: a conciliação corrige a assinatura pelo estado do
/// gateway (com Histórico) e o webhook trata estorno e chargeback.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class ConciliacaoCobrancaTests
{
    private readonly AplicacaoTesteFactory _factory;

    public ConciliacaoCobrancaTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class GatewayFalso : IGatewayCobranca
    {
        public AssinaturaGatewayConciliacao? Estado { get; set; }
        public bool Configurado => true;
        public Task<AssinaturaGatewayConciliacao?> ObterParaConciliacaoAsync(string assinaturaId, CancellationToken cancellationToken) => Task.FromResult(Estado);
        public Task<string> CriarClienteAsync(string nome, string cpfCnpj, string? email, string referenciaExterna, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> CriarAssinaturaMensalAsync(string clienteId, decimal valor, DateOnly primeiroVencimento, string descricao, string referenciaExterna, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ObterUrlFaturaPendenteAsync(string assinaturaId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CancelarAssinaturaAsync(string assinaturaId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AlterarValorAssinaturaAsync(string assinaturaId, decimal valor, string descricao, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FaturaGateway?> ObterProximaFaturaEmAbertoAsync(string assinaturaId, string? ignorarPagamentoId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RemoverFaturaAsync(string pagamentoId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DateOnly?> ObterProximoVencimentoAsync(string assinaturaId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AlterarProximoVencimentoAsync(string assinaturaId, DateOnly novoVencimento, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static ConciliacaoCobrancaService Servico(IServiceProvider sp, IGatewayCobranca gateway) => new(
        sp.GetRequiredService<AppDbContext>(),
        gateway,
        sp.GetRequiredService<IAuditLogWriter>(),
        sp.GetRequiredService<IBackgroundJobClient>(),
        Options.Create(new CobrancaOptions()),
        NullLogger<ConciliacaoCobrancaService>.Instance);

    private static async Task<(Tenant Conta, Assinatura Assinatura)> CriarContaAsync(AppDbContext db, StatusAssinatura status, string? asaasId = "sub_")
    {
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Conciliação " + s };
        db.Tenants.Add(conta);
        var assinatura = new Assinatura
        {
            TenantId = conta.Id,
            Status = status,
            AsaasClienteId = asaasId is null ? null : "cus_" + s,
            AsaasAssinaturaId = asaasId is null ? null : asaasId + s
        };
        db.Assinaturas.Add(assinatura);
        await db.SaveChangesAsync();
        return (conta, assinatura);
    }

    private static async Task LimparAsync(AppDbContext db, Guid tenantId)
    {
        await db.EventosCobranca.Where(e => e.TenantId == tenantId).ExecuteDeleteAsync();
        await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Assinaturas.Where(a => a.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Pagamento_que_o_webhook_perdeu_ativa_a_conta_com_historico()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var (conta, assinatura) = await CriarContaAsync(db, StatusAssinatura.Teste);
        try
        {
            var gateway = new GatewayFalso
            {
                Estado = new AssinaturaGatewayConciliacao(false,
                [
                    new FaturaConciliacao("pay_1", "RECEIVED", new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 9), "https://asaas/i/1", 49.90m),
                    new FaturaConciliacao("pay_2", "PENDING", new DateOnly(2026, 12, 10), null, "https://asaas/i/2", 49.90m)
                ])
            };

            var r = await Servico(sp, gateway).ConciliarAsync(conta.Id, operadorId: null, CancellationToken.None);
            Assert.True(r.Mudou);
            Assert.StartsWith("Corrigido pelo Asaas:", r.Mensagem);

            await db.Entry(assinatura).ReloadAsync();
            Assert.Equal(StatusAssinatura.Ativa, assinatura.Status);
            Assert.NotNull(assinatura.UltimoPagamentoEm);
            Assert.Equal("https://asaas/i/2", assinatura.UrlFaturaPendente);
            Assert.Equal(new DateOnly(2026, 12, 10), assinatura.VencimentoFaturaPendente);
            Assert.True(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(l => l.TenantId == conta.Id && l.Operacao == ConciliacaoCobrancaService.OperacaoHistorico));

            // Segunda vez: já está igual, nada muda e não grava Histórico de novo.
            var deNovo = await Servico(sp, gateway).ConciliarAsync(conta.Id, operadorId: null, CancellationToken.None);
            Assert.False(deNovo.Mudou);
            Assert.Equal(1, await db.AuditLogs.IgnoreQueryFilters().CountAsync(l => l.TenantId == conta.Id && l.Operacao == ConciliacaoCobrancaService.OperacaoHistorico));
        }
        finally
        {
            await LimparAsync(db, conta.Id);
        }
    }

    [Fact]
    public async Task Assinatura_encerrada_no_asaas_cancela_e_desconhecida_nao_muda()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var (conta, assinatura) = await CriarContaAsync(db, StatusAssinatura.Ativa);
        try
        {
            var desconhecida = await Servico(sp, new GatewayFalso { Estado = null }).ConciliarAsync(conta.Id, null, CancellationToken.None);
            Assert.False(desconhecida.Mudou);
            Assert.Contains("não encontrou", desconhecida.Mensagem);

            await Servico(sp, new GatewayFalso { Estado = new AssinaturaGatewayConciliacao(true, []) })
                .ConciliarAsync(conta.Id, Guid.NewGuid(), CancellationToken.None);
            await db.Entry(assinatura).ReloadAsync();
            Assert.Equal(StatusAssinatura.Cancelada, assinatura.Status);
            Assert.NotNull(assinatura.CanceladaEm);
        }
        finally
        {
            await LimparAsync(db, conta.Id);
        }
    }

    [Fact]
    public async Task Conta_sem_assinatura_no_asaas_nao_tem_o_que_conferir()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var (conta, _) = await CriarContaAsync(db, StatusAssinatura.Teste, asaasId: null);
        try
        {
            var r = await Servico(sp, new GatewayFalso()).ConciliarAsync(conta.Id, null, CancellationToken.None);
            Assert.False(r.Mudou);
            Assert.Contains("não tem assinatura no Asaas", r.Mensagem);
        }
        finally
        {
            await LimparAsync(db, conta.Id);
        }
    }

    [Fact]
    public async Task Webhook_de_estorno_e_chargeback_atrasa_e_contestacao_ganha_reativa()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var processar = sp.GetRequiredService<IProcessarWebhookAsaasUseCase>();
        var (conta, assinatura) = await CriarContaAsync(db, StatusAssinatura.Ativa);
        var sufixo = assinatura.AsaasAssinaturaId!;
        try
        {
            string Evento(string id, string tipo) => $$$"""
                {"id":"{{{id}}}_{{{sufixo}}}","event":"{{{tipo}}}",
                 "payment":{"object":"payment","id":"pay_{{{sufixo}}}","subscription":"{{{sufixo}}}","status":"REFUNDED","dueDate":"2026-11-10"}}
                """;

            Assert.Equal("pagamento desfeito (estorno ou contestação): atrasada",
                await processar.ExecutarAsync(Evento("evt_cb", "PAYMENT_CHARGEBACK_REQUESTED"), CancellationToken.None));
            await db.Entry(assinatura).ReloadAsync();
            Assert.Equal(StatusAssinatura.Atrasada, assinatura.Status);
            var desde = assinatura.AtrasadaDesde;
            Assert.NotNull(desde);

            // A disputa não reinicia a tolerância.
            await processar.ExecutarAsync(Evento("evt_disputa", "PAYMENT_CHARGEBACK_DISPUTE"), CancellationToken.None);
            await db.Entry(assinatura).ReloadAsync();
            Assert.Equal(desde, assinatura.AtrasadaDesde);

            Assert.Equal("pagamento restabelecido: assinatura ativa",
                await processar.ExecutarAsync(Evento("evt_ganha", "PAYMENT_AWAITING_CHARGEBACK_REVERSAL"), CancellationToken.None));
            await db.Entry(assinatura).ReloadAsync();
            Assert.Equal(StatusAssinatura.Ativa, assinatura.Status);
            Assert.Null(assinatura.AtrasadaDesde);

            await processar.ExecutarAsync(Evento("evt_estorno", "PAYMENT_REFUNDED"), CancellationToken.None);
            await db.Entry(assinatura).ReloadAsync();
            Assert.Equal(StatusAssinatura.Atrasada, assinatura.Status);
        }
        finally
        {
            await LimparAsync(db, conta.Id);
        }
    }
}
