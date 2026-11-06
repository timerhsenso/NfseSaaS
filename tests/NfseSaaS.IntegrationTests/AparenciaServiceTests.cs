using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Aparencia;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>Aparência com o banco real: cores só pelo administrador, preferências de cada um, cache limpo ao salvar.</summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class AparenciaServiceTests
{
    private readonly AplicacaoTesteFactory _factory;

    public AparenciaServiceTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class UsuarioFalso : ICurrentUser
    {
        public UsuarioFalso(Guid? id) => UserId = id;
        public Guid? UserId { get; }
        public string? IpAddress => "127.0.0.1";
    }

    private static AparenciaService Servico(IServiceProvider sp, Guid? usuarioId) => new(
        sp.GetRequiredService<AppDbContext>(),
        sp.GetRequiredService<ICurrentTenant>(),
        new UsuarioFalso(usuarioId),
        sp.GetRequiredService<IMemoryCache>(),
        sp.GetRequiredService<IAuditLogWriter>(),
        NullLogger<AparenciaService>.Instance);

    [Fact]
    public async Task Admin_muda_as_cores_da_conta_os_demais_so_as_proprias_preferencias()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");

        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Aparência " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        db.TenantIdOverrideDeSistema = conta.Id;
        var grupoAdmin = new Grupo { Nome = "Admin " + s, EhAdministrador = true };
        var grupoComum = new Grupo { Nome = "Comum " + s };
        db.Grupos.AddRange(grupoAdmin, grupoComum);
        await db.SaveChangesAsync();
        var admin = new ApplicationUser { TenantId = conta.Id, UserName = $"adm{s}@t.com", Email = $"adm{s}@t.com", GrupoId = grupoAdmin.Id };
        var comum = new ApplicationUser { TenantId = conta.Id, UserName = $"com{s}@t.com", Email = $"com{s}@t.com", GrupoId = grupoComum.Id };
        db.Users.AddRange(admin, comum);
        await db.SaveChangesAsync();
        db.TenantIdOverrideDeSistema = null;
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        try
        {
            // Sem login: padrão, sem erro.
            var anonimo = await Servico(sp, null).AtualAsync(CancellationToken.None);
            Assert.Equal("mico", anonimo.Paleta);

            // Usuário comum: nunca salvou; não muda as cores.
            var servicoComum = Servico(sp, comum.Id);
            var antes = await servicoComum.AtualAsync(CancellationToken.None);
            Assert.False(antes.Salvo);
            Assert.False(antes.PodeAlterarPaleta);
            var erro = await Assert.ThrowsAsync<RegraNegocioException>(() =>
                servicoComum.SalvarPaletaAsync(new SalvarPaletaRequest("oceano"), CancellationToken.None));
            Assert.Contains("administrador", erro.Message);

            // ...mas salva as preferências dele (e o cache é limpo).
            await servicoComum.SalvarPreferenciasAsync(new PreferenciasAparencia("gg", "confortavel", "escuro", true), CancellationToken.None);
            var depois = await servicoComum.AtualAsync(CancellationToken.None);
            Assert.True(depois.Salvo);
            Assert.Equal(new PreferenciasAparencia("gg", "confortavel", "escuro", true), depois.Preferencias);

            // Administrador muda as cores: vale para todos (cache da conta limpo).
            var servicoAdmin = Servico(sp, admin.Id);
            Assert.True((await servicoAdmin.AtualAsync(CancellationToken.None)).PodeAlterarPaleta);
            await Assert.ThrowsAsync<RegraNegocioException>(() =>
                servicoAdmin.SalvarPaletaAsync(new SalvarPaletaRequest("roxo"), CancellationToken.None));
            await servicoAdmin.SalvarPaletaAsync(new SalvarPaletaRequest("oceano"), CancellationToken.None);
            Assert.Equal("oceano", (await servicoComum.AtualAsync(CancellationToken.None)).Paleta);

            // As preferências do comum não afetam o administrador.
            Assert.Equal(PreferenciasAparencia.Padrao, (await servicoAdmin.AtualAsync(CancellationToken.None)).Preferencias);

            // Voltar à padrão grava nulo.
            await servicoAdmin.SalvarPaletaAsync(new SalvarPaletaRequest("mico"), CancellationToken.None);
            Assert.Null(await db.Tenants.AsNoTracking().Where(t => t.Id == conta.Id).Select(t => t.PaletaCores).SingleAsync());
            Assert.True(await db.AuditLogs.AnyAsync(l => l.Operacao == "AlterarPaletaCores"));
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
