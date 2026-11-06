using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Indicacao;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>Convite por e-mail do "Indique e ganhe" com o banco real: limites anti-spam e Reply-To.</summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class ConviteIndicacaoTests
{
    private const string Link = "https://app.teste/?ref=K7M2QX9A";
    private readonly AplicacaoTesteFactory _factory;

    public ConviteIndicacaoTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class UsuarioFalso : ICurrentUser
    {
        public UsuarioFalso(Guid id) => UserId = id;
        public Guid? UserId { get; }
        public string? IpAddress => "127.0.0.1";
    }

    private static ConvidarPorEmailUseCase Servico(IServiceProvider sp, Guid usuarioId) => new(
        sp.GetRequiredService<AppDbContext>(),
        sp.GetRequiredService<ICurrentTenant>(),
        new UsuarioFalso(usuarioId),
        sp.GetRequiredService<IEmailQueueService>(),
        sp.GetRequiredService<IAuditLogWriter>(),
        NullLogger<ConvidarPorEmailUseCase>.Instance);

    private static async Task<(Tenant Conta, ApplicationUser Usuario)> MontarAsync(AppDbContext db, string s, bool emailConfirmado = true)
    {
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Conta convite " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        var email = $"dono{s}@teste.com";
        var usuario = new ApplicationUser { TenantId = conta.Id, UserName = email, Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = emailConfirmado };
        db.TenantIdOverrideDeSistema = conta.Id;
        db.Users.Add(usuario);
        await db.SaveChangesAsync();
        db.TenantIdOverrideDeSistema = null;
        return (conta, usuario);
    }

    private static async Task LimparAsync(AppDbContext db, Guid tenantId)
    {
        db.ChangeTracker.Clear();
        await db.ConvitesIndicacao.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.EmailLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Tenants.Where(x => x.Id == tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Envia_com_reply_to_bloqueia_repetidos_usuarios_e_o_limite_do_dia()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var (conta, usuario) = await MontarAsync(db, s);
        var (outraConta, outroUsuario) = await MontarAsync(db, Guid.NewGuid().ToString("N"));

        try
        {
            sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);
            var servico = Servico(sp, usuario.Id);

            var r = await servico.ExecutarAsync(new ConvidarPorEmailRequest(
                $"a{s}@convidado.com, B{s}@Convidado.com\ninvalido, {usuario.Email}, {outroUsuario.Email}"), Link, CancellationToken.None);

            Assert.Equal(new[] { true, true }, r.Resultados.Where(x => x.Email.Contains("@convidado.com")).Select(x => x.Enviado));
            Assert.Equal(RegrasConviteIndicacao.MotivoInvalido, r.Resultados.Single(x => x.Email == "invalido").Motivo);
            Assert.Equal(RegrasConviteIndicacao.MotivoProprio, r.Resultados.Single(x => x.Email == usuario.Email).Motivo);
            Assert.Equal(RegrasConviteIndicacao.MotivoBloqueado, r.Resultados.Single(x => x.Email == outroUsuario.Email).Motivo);
            Assert.Equal(RegrasConviteIndicacao.LimitePorDia - 2, r.RestamHoje);

            var emails = await db.EmailLogs.AsNoTracking().Where(e => e.Tipo == TipoEmailCatalogo.ConviteIndicacao).ToListAsync();
            Assert.Equal(2, emails.Count);
            Assert.All(emails, e =>
            {
                Assert.Equal(usuario.Email, e.ResponderPara);
                Assert.Contains("ref=K7M2QX9A&amp;origem=convite-email", e.CorpoHtml);
            });

            // Mesmo endereço, agora por OUTRA conta: bloqueado (1 convite a cada 30 dias, de qualquer conta).
            sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(outraConta.Id);
            var deOutra = await Servico(sp, outroUsuario.Id).ExecutarAsync(new ConvidarPorEmailRequest($"a{s}@convidado.com"), Link, CancellationToken.None);
            Assert.Equal(RegrasConviteIndicacao.MotivoBloqueado, Assert.Single(deOutra.Resultados).Motivo);

            // Limite do dia: 8 restantes; pedir 10 envia 8 e recusa 2.
            sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);
            var dez = string.Join(",", Enumerable.Range(1, 10).Select(i => $"n{i}{s}@convidado.com"));
            var lote = await servico.ExecutarAsync(new ConvidarPorEmailRequest(dez), Link, CancellationToken.None);
            Assert.Equal(8, lote.Resultados.Count(x => x.Enviado));
            Assert.Equal(2, lote.Resultados.Count(x => x.Motivo == RegrasConviteIndicacao.MotivoLimite));
            Assert.Equal(0, lote.RestamHoje);

            var erro = await Assert.ThrowsAsync<RegraNegocioException>(() =>
                servico.ExecutarAsync(new ConvidarPorEmailRequest($"mais{s}@convidado.com"), Link, CancellationToken.None));
            Assert.Contains("convites de hoje", erro.Message);

            // A tela mostra os convites e o que resta.
            var tela = await sp.GetRequiredService<IObterIndicacaoUseCase>().ExecutarAsync(CancellationToken.None);
            Assert.Equal(RegrasConviteIndicacao.LimitePorDia, tela.Convites!.Count);
            Assert.Equal(0, tela.ConvitesRestamHoje);
        }
        finally
        {
            await LimparAsync(db, conta.Id);
            await LimparAsync(db, outraConta.Id);
        }
    }

    [Fact]
    public async Task Quem_nao_confirmou_o_email_nao_convida()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var (conta, usuario) = await MontarAsync(db, s, emailConfirmado: false);

        try
        {
            sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);
            var erro = await Assert.ThrowsAsync<RegraNegocioException>(() =>
                Servico(sp, usuario.Id).ExecutarAsync(new ConvidarPorEmailRequest($"x{s}@convidado.com"), Link, CancellationToken.None));
            Assert.Contains("Confirme o seu e-mail", erro.Message);
            Assert.False(await db.ConvitesIndicacao.AnyAsync());
        }
        finally
        {
            await LimparAsync(db, conta.Id);
        }
    }
}
