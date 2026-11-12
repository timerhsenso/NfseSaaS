using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Aparencia;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
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

    private static AparenciaService Servico(IServiceProvider sp, Guid? usuarioId, params Guid[] operadores) => new(
        sp.GetRequiredService<AppDbContext>(),
        sp.GetRequiredService<ICurrentTenant>(),
        new UsuarioFalso(usuarioId),
        sp.GetRequiredService<IMemoryCache>(),
        sp.GetRequiredService<IAuditLogWriter>(),
        NullLogger<AparenciaService>.Instance,
        Options.Create(new PlataformaOptions { UsuariosOperadoresIds = operadores.ToList() }));

    private static readonly byte[] Png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };
    private static readonly byte[] Jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 9, 9, 9 };

    [Fact]
    public async Task Logo_da_conta_so_admin_no_plano_escritorio_vale_para_todos()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");

        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Escritório " + s, Perfil = PerfilConta.Contador };
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
            var servicoAdmin = Servico(sp, admin.Id);
            var servicoComum = Servico(sp, comum.Id);
            Assert.Null((await servicoComum.AtualAsync(CancellationToken.None)).LogoVersao);
            Assert.True((await servicoAdmin.ObterAsync(CancellationToken.None)).LogoDisponivel);

            // Só o administrador; só imagem de verdade (pelo conteúdo).
            Assert.Contains("administrador", (await Assert.ThrowsAsync<RegraNegocioException>(() =>
                servicoComum.SalvarLogoAsync(Png, CancellationToken.None))).Message);
            Assert.Contains("PNG", (await Assert.ThrowsAsync<RegraNegocioException>(() =>
                servicoAdmin.SalvarLogoAsync("<svg onload=alert(1)>"u8.ToArray(), CancellationToken.None))).Message);

            // Enviou: todos da conta veem (cache da conta limpo).
            var versao = await servicoAdmin.SalvarLogoAsync(Png, CancellationToken.None);
            Assert.Equal(versao, (await servicoComum.AtualAsync(CancellationToken.None)).LogoVersao);
            var arquivo = await servicoComum.ObterLogoAsync(CancellationToken.None);
            Assert.Equal("image/png", arquivo!.TipoConteudo);
            Assert.Equal(Png, arquivo.Conteudo);

            // Trocar: continua um só, versão nova.
            var versao2 = await servicoAdmin.SalvarLogoAsync(Jpeg, CancellationToken.None);
            Assert.NotEqual(versao, versao2);
            Assert.Equal(1, await db.LogosConta.CountAsync(l => l.TenantId == conta.Id));
            Assert.Equal("image/jpeg", (await servicoComum.ObterLogoAsync(CancellationToken.None))!.TipoConteudo);

            // Remover: volta a marca do micoempresa.
            await servicoAdmin.RemoverLogoAsync(CancellationToken.None);
            Assert.Null((await servicoComum.AtualAsync(CancellationToken.None)).LogoVersao);
            Assert.Null(await servicoComum.ObterLogoAsync(CancellationToken.None));
            Assert.True(await db.AuditLogs.AnyAsync(l => l.Operacao == "AlterarLogoConta"));

            // Conta de empresa: fora do plano Escritório (o operador da plataforma pode, para conferir).
            await db.Tenants.Where(t => t.Id == conta.Id).ExecuteUpdateAsync(x => x.SetProperty(t => t.Perfil, PerfilConta.Empresa));
            Assert.False((await servicoAdmin.ObterAsync(CancellationToken.None)).LogoDisponivel);
            await Assert.ThrowsAsync<RegraNegocioException>(() => servicoAdmin.SalvarLogoAsync(Png, CancellationToken.None));
            Assert.NotNull(await Servico(sp, admin.Id, admin.Id).SalvarLogoAsync(Png, CancellationToken.None));
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.LogosConta.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Users.Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Grupos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }

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
