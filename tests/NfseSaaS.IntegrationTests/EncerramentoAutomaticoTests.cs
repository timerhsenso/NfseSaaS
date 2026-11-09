using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Application.UseCases.DadosConta;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Certificates;
using NfseSaaS.Infrastructure.Cobranca;
using NfseSaaS.Infrastructure.DadosConta;
using NfseSaaS.Infrastructure.Documents;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>D2 parte 2 com o banco real: avisa, avisa de novo e encerra; desligado não faz nada.</summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class EncerramentoAutomaticoTests
{
    private readonly AplicacaoTesteFactory _factory;

    public EncerramentoAutomaticoTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class SemUsuario : ICurrentUser
    {
        public Guid? UserId => null;
        public string? IpAddress => null;
    }

    private static EncerramentoAutomaticoService Servico(IServiceProvider sp, int? dias, Guid conta, bool isenta = false)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var fila = sp.GetRequiredService<IEmailQueueService>();
        var encerramento = new EncerramentoContaService(
            db, sp.GetRequiredService<ICurrentTenant>(), new SemUsuario(), new MemoryCache(new MemoryCacheOptions()),
            sp.GetRequiredService<IEmailSender>(), fila, sp.GetRequiredService<IAuditLogWriter>(),
            sp.GetRequiredService<IGatewayCobranca>(), sp.GetRequiredService<ExclusaoContaService>(),
            sp.GetRequiredService<CertificateFileStore>(), sp.GetRequiredService<ContratoDocumentoFileStore>(),
            sp.GetRequiredService<IRecurringJobManager>(), Options.Create(new PlataformaOptions()),
            NullLogger<EncerramentoContaService>.Instance);
        // Cobrança ligada só para a conta do teste (piloto), como numa conta real com cobrança.
        var cobranca = new CobrancaOptions { ContasPiloto = [conta], ContasIsentas = isenta ? [conta] : [] };
        return new EncerramentoAutomaticoService(
            db, encerramento, fila,
            Options.Create(new EncerramentoOptions { AutomaticoAposCancelarDias = dias }),
            Options.Create(cobranca), Options.Create(new PlataformaOptions()),
            Options.Create(new EmailOptions { AppBaseUrl = "https://app.teste" }),
            NullLogger<EncerramentoAutomaticoService>.Instance);
    }

    private static async Task<(Tenant Conta, DateTimeOffset CanceladaEm)> MontarAsync(AppDbContext db, string s, bool comAdmin = true)
    {
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Auto " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        var canceladaEm = new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);
        db.Assinaturas.Add(new Assinatura { TenantId = conta.Id, Status = StatusAssinatura.Cancelada, CanceladaEm = canceladaEm, AcessoAteAposCancelar = new DateOnly(2026, 11, 30) });
        await db.SaveChangesAsync();
        if (comAdmin)
        {
            db.TenantIdOverrideDeSistema = conta.Id;
            var grupo = new Grupo { Nome = "Admin " + s, EhAdministrador = true };
            db.Grupos.Add(grupo);
            await db.SaveChangesAsync();
            db.Users.Add(new ApplicationUser { TenantId = conta.Id, UserName = $"adm{s}@t.com", Email = $"adm{s}@t.com", GrupoId = grupo.Id });
            await db.SaveChangesAsync();
            db.TenantIdOverrideDeSistema = null;
        }
        db.ChangeTracker.Clear();
        return (conta, canceladaEm);
    }

    private static async Task LimparAsync(AppDbContext db, Guid tenantId)
    {
        db.ChangeTracker.Clear();
        await db.EmailLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Grupos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Assinaturas.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Tenants.Where(x => x.Id == tenantId).ExecuteDeleteAsync();
    }

    private static Task<List<string>> AvisosAsync(AppDbContext db, Guid tenantId) =>
        db.EmailLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Tipo == TipoEmailCatalogo.AvisoEncerramentoConta)
            .OrderBy(e => e.CreatedAt).Select(e => e.ChaveDeduplicacao!).ToListAsync();

    [Fact]
    public async Task Cancelada_recebe_dois_avisos_e_e_encerrada_na_data_prometida()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var (conta, _) = await MontarAsync(db, s);
        // 90 dias depois do fim do acesso (30/11/2026) = 28/02/2027.
        var prevista = new DateOnly(2027, 2, 28);

        try
        {
            // Desligado (padrão): nada acontece, nem com a data vencida.
            var desligado = await Servico(sp, null, conta.Id).ExecutarAsync(prevista.AddDays(30), CancellationToken.None);
            Assert.True(desligado.Desligado);
            Assert.Empty(await AvisosAsync(db, conta.Id));

            // Isenta: fica de fora.
            await Servico(sp, 90, conta.Id, isenta: true).ExecutarAsync(prevista, CancellationToken.None);
            Assert.Empty(await AvisosAsync(db, conta.Id));

            var servico = Servico(sp, 90, conta.Id);
            await servico.ExecutarAsync(prevista.AddDays(-16), CancellationToken.None);
            Assert.Empty(await AvisosAsync(db, conta.Id));

            // 15 dias antes: primeiro aviso, com a data na chave.
            var r1 = await servico.ExecutarAsync(prevista.AddDays(-15), CancellationToken.None);
            Assert.Equal(1, r1.Avisadas);
            var chaves = await AvisosAsync(db, conta.Id);
            Assert.Equal(prevista, RegrasEncerramentoAutomatico.DataNaChave(Assert.Single(chaves)));
            var corpo = await db.EmailLogs.IgnoreQueryFilters().Where(e => e.TenantId == conta.Id).Select(e => e.CorpoHtml).FirstAsync();
            Assert.Contains("28/02/2027", corpo);
            Assert.Contains("/MeusDados", corpo);
            Assert.Contains("/Assinatura", corpo);

            // Dias seguintes: não repete.
            await servico.ExecutarAsync(prevista.AddDays(-14), CancellationToken.None);
            Assert.Single(await AvisosAsync(db, conta.Id));

            // 3 dias antes: aviso final, uma vez.
            await servico.ExecutarAsync(prevista.AddDays(-3), CancellationToken.None);
            await servico.ExecutarAsync(prevista.AddDays(-2), CancellationToken.None);
            chaves = await AvisosAsync(db, conta.Id);
            Assert.Equal(2, chaves.Count);
            Assert.True(RegrasEncerramentoAutomatico.EhAvisoFinal(chaves[1]));

            // Na data: encerra (origem Automatico), com o expurgo marcado como no encerramento a pedido.
            var r2 = await servico.ExecutarAsync(prevista, CancellationToken.None);
            Assert.Equal(1, r2.Encerradas);
            var encerrada = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == conta.Id);
            Assert.NotNull(encerrada.EncerradaEm);
            Assert.False(encerrada.Ativo);
            Assert.Equal(OrigemEncerramento.Automatico, encerrada.EncerramentoOrigem);
            Assert.Equal(DataBrasilia.Hoje().AddDays(RegrasEncerramentoConta.DiasAteExpurgo), encerrada.ExpurgoPrevistoEm);
            Assert.StartsWith("Conta encerrada automaticamente", encerrada.MotivoBloqueio);
            Assert.True(await db.EmailLogs.IgnoreQueryFilters().AnyAsync(e => e.TenantId == conta.Id && e.Tipo == TipoEmailCatalogo.EncerramentoConta));

            // Já encerrada: a próxima passada não mexe.
            var r3 = await servico.ExecutarAsync(prevista.AddDays(1), CancellationToken.None);
            Assert.Equal(0, r3.Encerradas);
            Assert.Equal(0, r3.ComErro);
        }
        finally
        {
            await LimparAsync(db, conta.Id);
        }
    }

    [Fact]
    public async Task Ligado_tarde_avisa_antes_e_quem_assinou_de_novo_ou_nao_tem_admin_nao_e_encerrado()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var (tarde, _) = await MontarAsync(db, Guid.NewGuid().ToString("N"));
        var (semAdmin, _) = await MontarAsync(db, Guid.NewGuid().ToString("N"), comAdmin: false);
        var (voltou, _) = await MontarAsync(db, Guid.NewGuid().ToString("N"));
        await db.Assinaturas.Where(a => a.TenantId == voltou.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.Status, StatusAssinatura.Ativa));
        var hoje = new DateOnly(2027, 6, 1); // muito depois da data prevista (28/02/2027)

        try
        {
            // Configuração ligada tarde: primeiro avisa (data = hoje + 15), não encerra de cara.
            await Servico(sp, 90, tarde.Id).ExecutarAsync(hoje, CancellationToken.None);
            Assert.Null((await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == tarde.Id)).EncerradaEm);
            Assert.Equal(hoje.AddDays(15), RegrasEncerramentoAutomatico.DataNaChave(Assert.Single(await AvisosAsync(db, tarde.Id))));
            await Servico(sp, 90, tarde.Id).ExecutarAsync(hoje.AddDays(14), CancellationToken.None);
            Assert.Null((await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == tarde.Id)).EncerradaEm);
            await Servico(sp, 90, tarde.Id).ExecutarAsync(hoje.AddDays(15), CancellationToken.None);
            Assert.NotNull((await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == tarde.Id)).EncerradaEm);

            // Sem administrador com e-mail: ninguém para avisar → não encerra.
            var r = await Servico(sp, 90, semAdmin.Id).ExecutarAsync(hoje.AddDays(30), CancellationToken.None);
            Assert.True(r.SemDestinatario >= 1);
            Assert.Null((await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == semAdmin.Id)).EncerradaEm);

            // Assinou de novo: fora.
            await Servico(sp, 90, voltou.Id).ExecutarAsync(hoje.AddDays(30), CancellationToken.None);
            Assert.Empty(await AvisosAsync(db, voltou.Id));
            Assert.Null((await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == voltou.Id)).EncerradaEm);
        }
        finally
        {
            await LimparAsync(db, tarde.Id);
            await LimparAsync(db, semAdmin.Id);
            await LimparAsync(db, voltou.Id);
        }
    }
}
