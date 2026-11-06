using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Cobranca;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// Avisos de cobrança com o banco real: só administradores com e-mail
/// confirmado, nunca o mesmo aviso duas vezes, nada com a cobrança
/// desligada; faixa do painel; webhook guardando vencimento e valor.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class AvisosCobrancaTests
{
    private readonly AplicacaoTesteFactory _factory;

    public AvisosCobrancaTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private static AvisosCobrancaService Servico(IServiceProvider sp, CobrancaOptions opcoes) => new(
        sp.GetRequiredService<AppDbContext>(),
        sp.GetRequiredService<ICurrentTenant>(),
        sp.GetRequiredService<IEmailQueueService>(),
        sp.GetRequiredService<ICatalogoPlanos>(),
        Options.Create(opcoes),
        Options.Create(new EmailOptions { AppBaseUrl = "https://app.teste" }),
        NullLogger<AvisosCobrancaService>.Instance);

    private static async Task<(Tenant Conta, string EmailConfirmado)> MontarAsync(AppDbContext db, string s)
    {
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Conta avisos " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();

        db.TenantIdOverrideDeSistema = conta.Id;
        var grupo = new Grupo { Nome = "Admin " + s, EhAdministrador = true };
        db.Grupos.Add(grupo);
        await db.SaveChangesAsync();
        var confirmado = $"adm{s}@teste.com";
        db.Users.Add(new ApplicationUser { TenantId = conta.Id, UserName = confirmado, Email = confirmado, EmailConfirmed = true, GrupoId = grupo.Id });
        db.Users.Add(new ApplicationUser { TenantId = conta.Id, UserName = $"nc{s}@teste.com", Email = $"nc{s}@teste.com", EmailConfirmed = false, GrupoId = grupo.Id });
        await db.SaveChangesAsync();
        db.TenantIdOverrideDeSistema = null;

        db.Assinaturas.Add(new Assinatura
        {
            TenantId = conta.Id,
            Status = StatusAssinatura.Ativa,
            AsaasAssinaturaId = "sub_av_" + s,
            UltimoPagamentoEm = DateTimeOffset.UtcNow.AddDays(-20),
            UrlFaturaPendente = "https://asaas.teste/i/" + s,
            VencimentoFaturaPendente = DataBrasilia.Hoje().AddDays(1),
            ValorFaturaPendente = 29m
        });
        await db.SaveChangesAsync();
        return (conta, confirmado);
    }

    private static async Task LimparAsync(AppDbContext db, Guid tenantId)
    {
        db.ChangeTracker.Clear();
        await db.EmailLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.EventosCobranca.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Grupos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Assinaturas.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Tenants.Where(x => x.Id == tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Avisa_so_admin_confirmado_uma_vez_e_nada_com_a_cobranca_desligada()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var (conta, confirmado) = await MontarAsync(db, s);

        try
        {
            sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

            // Beta (cobrança desligada, conta não é piloto): nada sai.
            var desligada = await Servico(sp, new CobrancaOptions { Ativa = false }).ExecutarAsync(conta.Id, CancellationToken.None);
            Assert.Empty(desligada.Enviados);

            // Conta isenta: nada sai.
            var isenta = await Servico(sp, new CobrancaOptions { Ativa = true, ContasIsentas = { conta.Id } }).ExecutarAsync(conta.Id, CancellationToken.None);
            Assert.Empty(isenta.Enviados);

            var piloto = Servico(sp, new CobrancaOptions { Ativa = false, ContasPiloto = { conta.Id } });
            var primeira = await piloto.ExecutarAsync(conta.Id, CancellationToken.None);
            Assert.Equal(new[] { TipoAvisoCobranca.FaturaVenceAmanha }, primeira.Enviados);
            Assert.Equal(new[] { confirmado }, primeira.Destinatarios);

            var segunda = await piloto.ExecutarAsync(conta.Id, CancellationToken.None);
            Assert.Empty(segunda.Enviados);

            var emails = await db.EmailLogs.AsNoTracking().Where(e => e.Tipo == TipoEmailCatalogo.AvisoCobranca).ToListAsync();
            var email = Assert.Single(emails);
            Assert.Equal(confirmado, email.Destinatario);
            Assert.Equal("Sua fatura do micoempresa vence amanhã", email.Assunto);
            Assert.Contains("https://asaas.teste/i/" + s, email.CorpoHtml);
        }
        finally
        {
            await LimparAsync(db, conta.Id);
        }
    }

    [Fact]
    public async Task Faixa_aparece_so_com_cobranca_valendo_e_link_so_para_quem_pode_ver_a_assinatura()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var (conta, _) = await MontarAsync(db, s);

        try
        {
            sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

            IObterFaixaCobrancaUseCase desligada = Servico(sp, new CobrancaOptions { Ativa = false });
            Assert.Null(await desligada.ExecutarAsync(true, CancellationToken.None));

            IObterFaixaCobrancaUseCase piloto = Servico(sp, new CobrancaOptions { Ativa = false, ContasPiloto = { conta.Id } });
            var comLink = await piloto.ExecutarAsync(true, CancellationToken.None);
            Assert.Equal("info", comLink!.Nivel);
            Assert.Equal("https://asaas.teste/i/" + s, comLink.UrlPagar);
            Assert.Contains("vence amanhã", comLink.Mensagem);

            var semLink = await piloto.ExecutarAsync(false, CancellationToken.None);
            Assert.Null(semLink!.UrlPagar);
        }
        finally
        {
            await LimparAsync(db, conta.Id);
        }
    }

    [Fact]
    public async Task Webhook_guarda_vencimento_e_valor_da_fatura_e_pagamento_limpa()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var processar = sp.GetRequiredService<IProcessarWebhookAsaasUseCase>();
        var s = Guid.NewGuid().ToString("N");
        var (conta, _) = await MontarAsync(db, s);

        try
        {
            string Evento(string id, string tipo, string status) => $$$"""
                {"id":"{{{id}}}","event":"{{{tipo}}}",
                 "payment":{"object":"payment","id":"pay_{{{s}}}","subscription":"sub_av_{{{s}}}","status":"{{{status}}}","value":59.9,"dueDate":"2026-12-10","invoiceUrl":"https://asaas.teste/n/{{{s}}}"}}
                """;

            Assert.Equal("fatura em aberto registrada", await processar.ExecutarAsync(Evento("evt_av1_" + s, "PAYMENT_CREATED", "PENDING"), CancellationToken.None));
            var assinatura = await db.Assinaturas.AsNoTracking().SingleAsync(a => a.TenantId == conta.Id);
            Assert.Equal(new DateOnly(2026, 12, 10), assinatura.VencimentoFaturaPendente);
            Assert.Equal(59.9m, assinatura.ValorFaturaPendente);
            Assert.Equal("https://asaas.teste/n/" + s, assinatura.UrlFaturaPendente);

            Assert.Equal("assinatura ativa", await processar.ExecutarAsync(Evento("evt_av2_" + s, "PAYMENT_RECEIVED", "RECEIVED"), CancellationToken.None));
            assinatura = await db.Assinaturas.AsNoTracking().SingleAsync(a => a.TenantId == conta.Id);
            Assert.Null(assinatura.UrlFaturaPendente);
            Assert.Null(assinatura.VencimentoFaturaPendente);
            Assert.Null(assinatura.ValorFaturaPendente);
        }
        finally
        {
            await db.EventosCobranca.Where(e => e.EventoId.EndsWith(s)).ExecuteDeleteAsync();
            await LimparAsync(db, conta.Id);
        }
    }
}
