using Hangfire;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Application.UseCases.Indicacao;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Cobranca;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// "Indique e ganhe" com o banco real: o primeiro pagamento da conta indicada
/// gera os dois prêmios uma única vez, e a aplicação no gateway (falso aqui)
/// dispensa a próxima fatura, adia o vencimento quando ela ainda não existe,
/// guarda o prêmio de quem não paga e nunca repete depois de uma queda.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class PremioIndicacaoTests
{
    private readonly AplicacaoTesteFactory _factory;

    public PremioIndicacaoTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class GatewayFalso : IGatewayCobranca
    {
        public Dictionary<string, List<FaturaGateway>> Faturas { get; } = new();
        public Dictionary<string, DateOnly> ProximoVencimento { get; } = new();
        public List<string> Removidas { get; } = new();
        public Exception? FalharAoRemover { get; set; }

        public bool Configurado => true;

        public Task<FaturaGateway?> ObterProximaFaturaEmAbertoAsync(string assinaturaId, string? ignorarPagamentoId, CancellationToken cancellationToken) =>
            Task.FromResult(Faturas.GetValueOrDefault(assinaturaId, new())
                .Where(f => f.Id != ignorarPagamentoId)
                .OrderBy(f => f.Vencimento)
                .FirstOrDefault());

        public Task RemoverFaturaAsync(string pagamentoId, CancellationToken cancellationToken)
        {
            if (FalharAoRemover is { } erro)
                throw erro;
            Removidas.Add(pagamentoId);
            foreach (var lista in Faturas.Values)
                lista.RemoveAll(f => f.Id == pagamentoId);
            return Task.CompletedTask;
        }

        public Task<DateOnly?> ObterProximoVencimentoAsync(string assinaturaId, CancellationToken cancellationToken) =>
            Task.FromResult<DateOnly?>(ProximoVencimento.TryGetValue(assinaturaId, out var d) ? d : null);

        public Task AlterarProximoVencimentoAsync(string assinaturaId, DateOnly novoVencimento, CancellationToken cancellationToken)
        {
            ProximoVencimento[assinaturaId] = novoVencimento;
            return Task.CompletedTask;
        }

        public Task<string> CriarClienteAsync(string nome, string cpfCnpj, string? email, string referenciaExterna, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> CriarAssinaturaMensalAsync(string clienteId, decimal valor, DateOnly primeiroVencimento, string descricao, string referenciaExterna, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ObterUrlFaturaPendenteAsync(string assinaturaId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AlterarValorAssinaturaAsync(string assinaturaId, decimal valor, string descricao, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CancelarAssinaturaAsync(string assinaturaId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static PremioIndicacaoService Servico(IServiceProvider sp, IGatewayCobranca gateway) => new(
        sp.GetRequiredService<AppDbContext>(),
        gateway,
        sp.GetRequiredService<IAuditLogWriter>(),
        sp.GetRequiredService<IBackgroundJobClient>(),
        Options.Create(new CobrancaOptions()),
        NullLogger<PremioIndicacaoService>.Instance);

    private static string Pagamento(string eventoId, string tipo, string pagamentoId, string assinaturaId) => $$$"""
        {"id":"{{{eventoId}}}","event":"{{{tipo}}}","payment":{"object":"payment","id":"{{{pagamentoId}}}","subscription":"{{{assinaturaId}}}","status":"RECEIVED","dueDate":"2026-11-10","invoiceUrl":"https://asaas/i/{{{pagamentoId}}}"}}
        """;

    [Fact]
    public async Task Primeiro_pagamento_da_indicada_premia_os_dois_lados_uma_vez_e_aplica_sem_repetir()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var webhook = sp.GetRequiredService<IProcessarWebhookAsaasUseCase>();

        var s = Guid.NewGuid().ToString("N");
        var indicador = new Tenant { Documento = s[..14], RazaoSocial = "Indicador " + s };
        var indicada = new Tenant { Documento = s[14..28], RazaoSocial = "Indicada " + s };
        db.Tenants.AddRange(indicador, indicada);
        await db.SaveChangesAsync();
        indicada.IndicadoPorTenantId = indicador.Id;
        var subIndicada = "sub_a_" + s;
        var subIndicador = "sub_b_" + s;
        var assinaturaIndicada = new Assinatura { TenantId = indicada.Id, AsaasAssinaturaId = subIndicada, AsaasClienteId = "cus_a_" + s };
        // Quem indicou ainda está no teste: sem assinatura no Asaas.
        var assinaturaIndicador = new Assinatura { TenantId = indicador.Id };
        db.Assinaturas.AddRange(assinaturaIndicada, assinaturaIndicador);
        await db.SaveChangesAsync();

        try
        {
            // 1) Cartão: CONFIRMED e depois RECEIVED do mesmo pagamento → um par de prêmios só.
            var r1 = await webhook.ExecutarAsync(Pagamento("evt_1_" + s, "PAYMENT_CONFIRMED", "pay_1_" + s, subIndicada), CancellationToken.None);
            var r2 = await webhook.ExecutarAsync(Pagamento("evt_2_" + s, "PAYMENT_RECEIVED", "pay_1_" + s, subIndicada), CancellationToken.None);
            var r3 = await webhook.ExecutarAsync(Pagamento("evt_3_" + s, "PAYMENT_RECEIVED", "pay_9_" + s, subIndicada), CancellationToken.None);

            Assert.Equal("assinatura ativa; prêmio de indicação registrado", r1);
            Assert.Equal("assinatura ativa", r2);
            Assert.Equal("assinatura ativa", r3);

            var premios = await db.PremiosIndicacao.Where(p => p.IndicadoTenantId == indicada.Id).ToListAsync();
            Assert.Equal(2, premios.Count);
            Assert.All(premios, p => Assert.Equal(StatusPremioIndicacao.Pendente, p.Status));
            Assert.All(premios, p => Assert.Equal("pay_1_" + s, p.AsaasPagamentoOrigemId));
            var premioIndicada = premios.Single(p => p.Papel == PapelPremioIndicacao.ContaIndicada);
            var premioIndicador = premios.Single(p => p.Papel == PapelPremioIndicacao.QuemIndicou);
            Assert.Equal(indicada.Id, premioIndicada.TenantId);
            Assert.Equal(indicador.Id, premioIndicador.TenantId);

            // 2) Aplicação: a indicada tem a fatura de dezembro em aberto (a de origem não conta).
            var gateway = new GatewayFalso();
            gateway.Faturas[subIndicada] = new()
            {
                new FaturaGateway("pay_1_" + s, new DateOnly(2026, 11, 10), "https://asaas/i/pay_1_" + s),
                new FaturaGateway("pay_2_" + s, new DateOnly(2026, 12, 10), "https://asaas/i/pay_2_" + s)
            };
            var servico = (IAplicarPremiosIndicacaoUseCase)Servico(sp, gateway);

            var resultado = await servico.ExecutarAsync(CancellationToken.None);
            Assert.True(resultado.Aplicados >= 1);
            Assert.Equal(new[] { "pay_2_" + s }, gateway.Removidas);

            await db.Entry(premioIndicada).ReloadAsync();
            await db.Entry(premioIndicador).ReloadAsync();
            Assert.Equal(StatusPremioIndicacao.Aplicado, premioIndicada.Status);
            Assert.Equal(new DateOnly(2026, 12, 10), premioIndicada.VencimentoDispensado);
            Assert.Equal("pay_2_" + s, premioIndicada.AsaasPagamentoDispensadoId);
            Assert.Equal(StatusPremioIndicacao.Pendente, premioIndicador.Status);
            Assert.Equal(RegrasPremioIndicacao.MotivoSemAssinatura, premioIndicador.Detalhe);
            Assert.True(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.TenantId == indicada.Id && a.Operacao == "PremioIndicacaoAplicado"));

            // Rodar de novo não dispensa outra fatura.
            await servico.ExecutarAsync(CancellationToken.None);
            Assert.Single(gateway.Removidas);

            // 3) Quem indicou passa a pagar; a próxima fatura ainda não foi gerada → adia o vencimento.
            assinaturaIndicador.AsaasAssinaturaId = subIndicador;
            assinaturaIndicador.Status = StatusAssinatura.Ativa;
            await db.SaveChangesAsync();
            gateway.ProximoVencimento[subIndicador] = new DateOnly(2027, 1, 5);

            await servico.ExecutarAsync(CancellationToken.None);
            await db.Entry(premioIndicador).ReloadAsync();
            Assert.Equal(StatusPremioIndicacao.Aplicado, premioIndicador.Status);
            Assert.Equal(new DateOnly(2027, 2, 5), gateway.ProximoVencimento[subIndicador]);
            Assert.Equal(new DateOnly(2027, 1, 5), premioIndicador.VencimentoDispensado);

            // 4) A tela "Indique e ganhe" de quem indicou mostra o prêmio ganho.
            sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(indicador.Id);
            var tela = await sp.GetRequiredService<IObterIndicacaoUseCase>().ExecutarAsync(CancellationToken.None);
            Assert.Equal(StatusPremioIndicacao.Aplicado, Assert.Single(tela.Indicadas).Premio);
        }
        finally
        {
            await LimparAsync(db, indicador.Id, indicada.Id, s);
        }
    }

    [Fact]
    public async Task Recusa_tenta_de_novo_queda_vira_erro_e_operador_reabre()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();

        var s = Guid.NewGuid().ToString("N");
        var indicador = new Tenant { Documento = s[..14], RazaoSocial = "Indicador " + s };
        var indicada = new Tenant { Documento = s[14..28], RazaoSocial = "Indicada " + s };
        db.Tenants.AddRange(indicador, indicada);
        await db.SaveChangesAsync();
        var sub = "sub_c_" + s;
        db.Assinaturas.Add(new Assinatura { TenantId = indicada.Id, AsaasAssinaturaId = sub, Status = StatusAssinatura.Ativa });
        var premio = new PremioIndicacao { TenantId = indicada.Id, IndicadoTenantId = indicada.Id, Papel = PapelPremioIndicacao.ContaIndicada };
        db.PremiosIndicacao.Add(premio);
        await db.SaveChangesAsync();

        try
        {
            var gateway = new GatewayFalso();
            gateway.Faturas[sub] = new() { new FaturaGateway("pay_x_" + s, new DateOnly(2026, 12, 1), null) };
            var servico = Servico(sp, gateway);
            var aplicar = (IAplicarPremiosIndicacaoUseCase)servico;

            // Recusa do Asaas: nada mudou lá, volta para a fila.
            gateway.FalharAoRemover = new RegraNegocioException("Pagamento: cobrança não pode ser removida.");
            await aplicar.ExecutarAsync(CancellationToken.None);
            await db.Entry(premio).ReloadAsync();
            Assert.Equal(StatusPremioIndicacao.Pendente, premio.Status);
            Assert.Equal(1, premio.Tentativas);

            // Queda sem resposta: pode ter removido → Erro, nunca repete sozinho.
            gateway.FalharAoRemover = new HttpRequestException("timeout");
            await aplicar.ExecutarAsync(CancellationToken.None);
            await db.Entry(premio).ReloadAsync();
            Assert.Equal(StatusPremioIndicacao.Erro, premio.Status);
            await aplicar.ExecutarAsync(CancellationToken.None);
            Assert.Empty(gateway.Removidas);

            // Operador reabre e a próxima passada aplica.
            gateway.FalharAoRemover = null;
            await ((IReabrirPremioIndicacaoUseCase)servico).ExecutarAsync(premio.Id, null, CancellationToken.None);
            await db.Entry(premio).ReloadAsync();
            Assert.Equal(StatusPremioIndicacao.Pendente, premio.Status);
            await Assert.ThrowsAsync<RegraNegocioException>(() => ((IReabrirPremioIndicacaoUseCase)servico).ExecutarAsync(premio.Id, null, CancellationToken.None));

            await aplicar.ExecutarAsync(CancellationToken.None);
            await db.Entry(premio).ReloadAsync();
            Assert.Equal(StatusPremioIndicacao.Aplicado, premio.Status);
            Assert.Equal(new[] { "pay_x_" + s }, gateway.Removidas);

            // "Aplicando" recente: pode ser outra execução no meio → não mexe.
            premio.Status = StatusPremioIndicacao.Aplicando;
            await db.SaveChangesAsync();
            await aplicar.ExecutarAsync(CancellationToken.None);
            await db.Entry(premio).ReloadAsync();
            Assert.Equal(StatusPremioIndicacao.Aplicando, premio.Status);

            // "Aplicando" antigo (processo caiu no meio): vira Erro, sem chamar o gateway.
            // (direto no banco: o SaveChanges carimba UpdatedAt com a hora atual)
            var antigo = DateTimeOffset.UtcNow - PremioIndicacaoService.TempoMaximoAplicando - TimeSpan.FromMinutes(1);
            await db.PremiosIndicacao.Where(p => p.Id == premio.Id).ExecuteUpdateAsync(u => u.SetProperty(p => p.UpdatedAt, antigo));
            await aplicar.ExecutarAsync(CancellationToken.None);
            await db.Entry(premio).ReloadAsync();
            Assert.Equal(StatusPremioIndicacao.Erro, premio.Status);
            Assert.Single(gateway.Removidas);

            var listados = await ((IListarPremiosIndicacaoPlataformaUseCase)servico).ExecutarAsync(CancellationToken.None);
            Assert.Contains(listados, p => p.Id == premio.Id && p.Conta == indicada.RazaoSocial);
        }
        finally
        {
            await LimparAsync(db, indicador.Id, indicada.Id, s);
        }
    }

    private static async Task LimparAsync(AppDbContext db, Guid indicadorId, Guid indicadaId, string sufixo)
    {
        var ids = new[] { indicadorId, indicadaId };
        await db.PremiosIndicacao.Where(p => ids.Contains(p.TenantId) || ids.Contains(p.IndicadoTenantId)).ExecuteDeleteAsync();
        await db.AuditLogs.IgnoreQueryFilters().Where(a => ids.Contains(a.TenantId)).ExecuteDeleteAsync();
        await db.EventosCobranca.Where(e => e.EventoId.EndsWith(sufixo)).ExecuteDeleteAsync();
        await db.Assinaturas.Where(a => ids.Contains(a.TenantId)).ExecuteDeleteAsync();
        await db.Tenants.Where(t => t.Id == indicadaId).ExecuteDeleteAsync();
        await db.Tenants.Where(t => t.Id == indicadorId).ExecuteDeleteAsync();
    }
}
