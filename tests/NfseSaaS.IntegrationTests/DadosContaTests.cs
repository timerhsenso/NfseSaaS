using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Conta;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>Dados da conta com o banco real: WhatsApp só pelo administrador, autorização (LGPD) e lembrete do painel.</summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class DadosContaTests
{
    private readonly AplicacaoTesteFactory _factory;

    public DadosContaTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class UsuarioFalso : ICurrentUser
    {
        public UsuarioFalso(Guid? id) => UserId = id;
        public Guid? UserId { get; }
        public string? IpAddress => "127.0.0.1";
    }

    private static DadosContaService Servico(IServiceProvider sp, Guid usuarioId) => new(
        sp.GetRequiredService<AppDbContext>(),
        sp.GetRequiredService<ICurrentTenant>(),
        new UsuarioFalso(usuarioId),
        sp.GetRequiredService<IAuditLogWriter>());

    [Fact]
    public async Task WhatsApp_so_pelo_admin_com_autorizacao_e_lembrete_que_adia()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");

        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Conta " + s, NomeFantasia = "Fantasia " + s, Perfil = PerfilConta.Empresa };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        db.TenantIdOverrideDeSistema = conta.Id;
        var grupoAdmin = new Grupo { Nome = "Admin " + s, EhAdministrador = true };
        var grupoComum = new Grupo { Nome = "Comum " + s };
        db.Grupos.AddRange(grupoAdmin, grupoComum);
        await db.SaveChangesAsync();
        var admin = new ApplicationUser { TenantId = conta.Id, UserName = $"adm{s}@t.com", Email = $"adm{s}@t.com", GrupoId = grupoAdmin.Id };
        var comum = new ApplicationUser
        {
            TenantId = conta.Id, UserName = $"com{s}@t.com", Email = $"com{s}@t.com", GrupoId = grupoComum.Id,
            ConvidadoEm = DateTimeOffset.UtcNow
        };
        db.Users.AddRange(admin, comum);
        await db.SaveChangesAsync();
        db.TenantIdOverrideDeSistema = null;
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        try
        {
            var servicoAdmin = Servico(sp, admin.Id);
            var servicoComum = Servico(sp, comum.Id);

            // Cadastro: nome fantasia, e-mail de quem se cadastrou (não o convidado).
            var dados = await servicoAdmin.ObterAsync(CancellationToken.None);
            Assert.Equal("Fantasia " + s, dados.Nome);
            Assert.Equal(admin.Email, dados.EmailResponsavel);
            Assert.Null(dados.WhatsApp);
            Assert.True(dados.PodeAlterar);
            Assert.True(dados.MostrarLembreteWhatsApp);

            // Usuário comum: só vê, não muda e não recebe o lembrete.
            var dadosComum = await servicoComum.ObterAsync(CancellationToken.None);
            Assert.False(dadosComum.PodeAlterar);
            Assert.False(dadosComum.MostrarLembreteWhatsApp);
            Assert.Contains("administrador", (await Assert.ThrowsAsync<RegraNegocioException>(() =>
                servicoComum.SalvarContatoAsync(new SalvarContatoContaRequest("(71) 99999-8888", true), CancellationToken.None))).Message);

            // Número inválido não grava.
            await Assert.ThrowsAsync<RegraNegocioException>(() =>
                servicoAdmin.SalvarContatoAsync(new SalvarContatoContaRequest("(71) 3333-4444", true), CancellationToken.None));

            // Admin grava com autorização; a data da autorização fica guardada.
            var salvo = await servicoAdmin.SalvarContatoAsync(new SalvarContatoContaRequest("71 99999-8888", true), CancellationToken.None);
            Assert.Equal("(71) 99999-8888", salvo.WhatsApp);
            Assert.True(salvo.WhatsAppAutorizado);
            Assert.False(salvo.MostrarLembreteWhatsApp);
            var autorizadoEm = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == conta.Id)).WhatsAppAutorizadoEm;
            Assert.NotNull(autorizadoEm);
            Assert.Equal("5571999998888", (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == conta.Id)).WhatsApp);

            // Troca o número mantendo a autorização: a data original não muda.
            await servicoAdmin.SalvarContatoAsync(new SalvarContatoContaRequest("(71) 98888-7777", true), CancellationToken.None);
            Assert.Equal(autorizadoEm, (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == conta.Id)).WhatsAppAutorizadoEm);

            // Tira a autorização: fica só para contato.
            var semAvisos = await servicoAdmin.SalvarContatoAsync(new SalvarContatoContaRequest("(71) 98888-7777", false), CancellationToken.None);
            Assert.False(semAvisos.WhatsAppAutorizado);

            // Apaga o número: a autorização vai junto e o lembrete volta.
            var apagado = await servicoAdmin.SalvarContatoAsync(new SalvarContatoContaRequest("", true), CancellationToken.None);
            Assert.Null(apagado.WhatsApp);
            Assert.False(apagado.WhatsAppAutorizado);
            Assert.True(apagado.MostrarLembreteWhatsApp);

            // "Agora não": o lembrete some por 30 dias.
            await servicoAdmin.AdiarLembreteWhatsAppAsync(CancellationToken.None);
            Assert.False((await servicoAdmin.ObterAsync(CancellationToken.None)).MostrarLembreteWhatsApp);
            var adiadoAte = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == conta.Id)).LembreteWhatsAppAdiadoAte;
            Assert.InRange(adiadoAte!.Value, DateTimeOffset.UtcNow.AddDays(IDadosContaUseCase.DiasAdiarLembrete - 1), DateTimeOffset.UtcNow.AddDays(IDadosContaUseCase.DiasAdiarLembrete + 1));

            // Prazo vencido: volta a aparecer.
            await db.Tenants.Where(t => t.Id == conta.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(t => t.LembreteWhatsAppAdiadoAte, DateTimeOffset.UtcNow.AddMinutes(-1)));
            Assert.True((await servicoAdmin.ObterAsync(CancellationToken.None)).MostrarLembreteWhatsApp);
            Assert.True(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(l => l.TenantId == conta.Id && l.Operacao == "AlterarContatoConta"));
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Users.Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Grupos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }
}
