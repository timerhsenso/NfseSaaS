using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.UseCases.Indicacao;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>B7 com o banco real: convites e e-mails de convite saem depois de 12 meses; o resto fica.</summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class RetencaoConvitesIndicacaoTests
{
    private readonly AplicacaoTesteFactory _factory;

    public RetencaoConvitesIndicacaoTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Convite_e_email_de_convite_com_mais_de_12_meses_sao_apagados_e_os_recentes_ficam()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Retenção convites " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();

        var agora = DateTimeOffset.UtcNow;
        var velho = agora.AddMonths(-12).AddDays(-1);
        var recente = agora.AddMonths(-12).AddDays(1);

        db.TenantIdOverrideDeSistema = conta.Id;
        var conviteVelho = new ConviteIndicacao { TenantId = conta.Id, Email = $"velho{s}@teste.com", EnviadoPorUsuarioId = Guid.NewGuid(), CreatedAt = velho };
        var conviteRecente = new ConviteIndicacao { TenantId = conta.Id, Email = $"recente{s}@teste.com", EnviadoPorUsuarioId = Guid.NewGuid(), CreatedAt = recente };
        var emailVelho = new EmailLog { TenantId = conta.Id, Tipo = TipoEmailCatalogo.ConviteIndicacao, Destinatario = conviteVelho.Email, Assunto = "x", CorpoHtml = "x", CreatedAt = velho };
        var emailRecente = new EmailLog { TenantId = conta.Id, Tipo = TipoEmailCatalogo.ConviteIndicacao, Destinatario = conviteRecente.Email, Assunto = "x", CorpoHtml = "x", CreatedAt = recente };
        var outroTipoVelho = new EmailLog { TenantId = conta.Id, Tipo = "Teste", Destinatario = $"cliente{s}@teste.com", Assunto = "x", CorpoHtml = "x", CreatedAt = velho };
        db.ConvitesIndicacao.AddRange(conviteVelho, conviteRecente);
        db.EmailLogs.AddRange(emailVelho, emailRecente, outroTipoVelho);
        await db.SaveChangesAsync();
        db.TenantIdOverrideDeSistema = null;
        db.ChangeTracker.Clear();

        try
        {
            var limpar = sp.GetRequiredService<ILimparConvitesIndicacaoUseCase>();
            var r = await limpar.ExecutarAsync(agora, CancellationToken.None);
            Assert.True(r.Convites >= 1);
            Assert.True(r.EmailsEnviados >= 1);

            var convites = await db.ConvitesIndicacao.IgnoreQueryFilters().AsNoTracking().Where(c => c.TenantId == conta.Id).Select(c => c.Id).ToListAsync();
            Assert.Equal([conviteRecente.Id], convites);

            var emails = await db.EmailLogs.IgnoreQueryFilters().AsNoTracking().Where(e => e.TenantId == conta.Id).Select(e => e.Id).ToListAsync();
            Assert.Equal(2, emails.Count);
            Assert.Contains(emailRecente.Id, emails);
            Assert.Contains(outroTipoVelho.Id, emails); // só o e-mail de CONVITE segue a retenção dos convites

            // Segunda passada no mesmo dia: nada mais desta conta para apagar.
            await limpar.ExecutarAsync(agora, CancellationToken.None);
            Assert.Equal(1, await db.ConvitesIndicacao.IgnoreQueryFilters().CountAsync(c => c.TenantId == conta.Id));
        }
        finally
        {
            await db.ConvitesIndicacao.IgnoreQueryFilters().Where(c => c.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.EmailLogs.IgnoreQueryFilters().Where(e => e.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(t => t.Id == conta.Id).ExecuteDeleteAsync();
        }
    }
}
