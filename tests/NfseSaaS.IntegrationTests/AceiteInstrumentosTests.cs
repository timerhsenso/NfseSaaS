using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.Legal;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Domain.Legal;
using NfseSaaS.Infrastructure.Certificates;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Legal;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// JUR1 — aceites separados da versão 1.2: DPA da conta (só o administrador,
/// obrigatório a partir de 09/12/2026), autorização do certificado A1 e da
/// emissão automática por empresa, com hash do texto, revogação ao trocar e
/// comprovante em PDF. A data é simulada para não depender do dia do teste.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class AceiteInstrumentosTests
{
    private const string SenhaPfx = "senha-do-pfx-jur1";
    private static readonly DateOnly Publicacao12 = new(2026, 11, 24);
    private static readonly DateOnly Vigencia12 = new(2026, 12, 9);

    private readonly AplicacaoTesteFactory _factory;

    public AceiteInstrumentosTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Antes_da_publicacao_da_1_2_nada_fica_pendente()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var cenario = await CriarCenarioAsync(sp, db);
        try
        {
            var servico = Servico(sp, db, cenario.Pasta, new DateOnly(2026, 11, 23));
            Assert.False((await servico.PendenciasAsync(cenario.Admin.Id, CancellationToken.None)).AlgumaPendente);
            Assert.False(await servico.DpaObrigatorioPendenteAsync(cenario.Admin.Id, CancellationToken.None));
        }
        finally
        {
            await LimparAsync(db, cenario);
        }
    }

    [Fact]
    public async Task Depois_da_publicacao_o_admin_ve_dpa_certificado_e_automatica_e_o_dpa_so_obriga_na_vigencia()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var cenario = await CriarCenarioAsync(sp, db);
        try
        {
            var publicado = Servico(sp, db, cenario.Pasta, Publicacao12);
            var p = await publicado.PendenciasAsync(cenario.Admin.Id, CancellationToken.None);
            Assert.True(p.EhAdministrador);
            Assert.True(p.DpaPendente);
            Assert.False(p.DpaObrigatorio);
            Assert.Equal(cenario.ComCertificado.Id, Assert.Single(p.Certificados).EmpresaId);
            Assert.Equal(cenario.NoAutomatico.Id, Assert.Single(p.EmissaoAutomatica).EmpresaId);
            Assert.False(await publicado.DpaObrigatorioPendenteAsync(cenario.Admin.Id, CancellationToken.None));

            // Usuário que não é administrador não aceita pela conta.
            Assert.False((await publicado.PendenciasAsync(cenario.Comum.Id, CancellationToken.None)).AlgumaPendente);
            await Assert.ThrowsAsync<RegraNegocioException>(() => publicado.RegistrarDpaAsync(cenario.Comum.Id, OrigemAceiteInstrumento.Atualizacao, CancellationToken.None));

            var vigente = Servico(sp, db, cenario.Pasta, Vigencia12);
            Assert.True((await vigente.PendenciasAsync(cenario.Admin.Id, CancellationToken.None)).DpaObrigatorio);
            Assert.True(await vigente.DpaObrigatorioPendenteAsync(cenario.Admin.Id, CancellationToken.None));
            Assert.False(await vigente.DpaObrigatorioPendenteAsync(cenario.Comum.Id, CancellationToken.None));

            await vigente.RegistrarDpaAsync(cenario.Admin.Id, OrigemAceiteInstrumento.Atualizacao, CancellationToken.None);
            await vigente.RegistrarAutorizacaoCertificadoAsync(cenario.Admin.Id, cenario.ComCertificado.Id, OrigemAceiteInstrumento.Atualizacao, CancellationToken.None);
            await vigente.RegistrarAutorizacaoEmissaoAutomaticaAsync(cenario.Admin.Id, cenario.NoAutomatico.Id, OrigemAceiteInstrumento.Atualizacao, CancellationToken.None);
            await db.SaveChangesAsync();

            Assert.False((await vigente.PendenciasAsync(cenario.Admin.Id, CancellationToken.None)).AlgumaPendente);
            Assert.False(await Servico(sp, db, cenario.Pasta, Vigencia12).DpaObrigatorioPendenteAsync(cenario.Admin.Id, CancellationToken.None));
            Assert.True(await vigente.AutorizacaoVigenteAsync(TipoDocumentoLegal.AutorizacaoCertificado, cenario.ComCertificado.Id, CancellationToken.None));
            Assert.True(await vigente.AutorizacaoVigenteAsync(TipoDocumentoLegal.AutorizacaoEmissaoAutomatica, cenario.NoAutomatico.Id, CancellationToken.None));

            var aceites = await db.AceitesInstrumentos.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.TenantId == cenario.Conta.Id).ToListAsync();
            Assert.Equal(3, aceites.Count);
            Assert.All(aceites, a =>
            {
                Assert.Equal("1.0", a.Versao);
                Assert.Equal(TextosInstrumentos.ObterObrigatorio(a.Tipo, a.Versao).Hash, a.HashTexto);
                Assert.Equal(cenario.Admin.Email, a.UsuarioEmail);
                Assert.Equal("Administrador", a.PerfilUsuario);
            });
            var a1 = Assert.Single(aceites, a => a.Tipo == TipoDocumentoLegal.AutorizacaoCertificado);
            Assert.Equal(cenario.ImpressaoDigital, a1.CertificadoImpressaoDigital);
            Assert.Equal(cenario.ComCertificado.Cnpj, a1.CnpjEmpresa);
            Assert.Equal(cenario.Conta.Documento, Assert.Single(aceites, a => a.Tipo == TipoDocumentoLegal.Dpa).CnpjEmpresa);

            Assert.True(await db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(x => x.TenantId == cenario.Conta.Id && x.Operacao == "AceitarInstrumento") >= 3);
        }
        finally
        {
            await LimparAsync(db, cenario);
        }
    }

    [Fact]
    public async Task Novo_aceite_revoga_o_anterior_e_o_comprovante_traz_o_texto()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var cenario = await CriarCenarioAsync(sp, db);
        try
        {
            var servico = Servico(sp, db, cenario.Pasta, Publicacao12);
            await servico.RegistrarAutorizacaoCertificadoAsync(cenario.Admin.Id, cenario.ComCertificado.Id, OrigemAceiteInstrumento.Certificado, CancellationToken.None);
            await db.SaveChangesAsync();
            await servico.RegistrarAutorizacaoCertificadoAsync(cenario.Admin.Id, cenario.ComCertificado.Id, OrigemAceiteInstrumento.Certificado, CancellationToken.None);
            await db.SaveChangesAsync();

            var a1 = await db.AceitesInstrumentos.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.TenantId == cenario.Conta.Id).OrderBy(a => a.AceitoEm).ToListAsync();
            Assert.Equal(2, a1.Count);
            Assert.NotNull(a1[0].RevogadoEm);
            Assert.Null(a1[1].RevogadoEm);

            Assert.True(await servico.RevogarAsync(TipoDocumentoLegal.AutorizacaoCertificado, cenario.ComCertificado.Id, cenario.Admin.Id, "Certificado removido.", CancellationToken.None));
            await db.SaveChangesAsync();
            Assert.False(await servico.AutorizacaoVigenteAsync(TipoDocumentoLegal.AutorizacaoCertificado, cenario.ComCertificado.Id, CancellationToken.None));

            // A lista mostra os aceites da conta para o administrador e nada para o usuário comum.
            var lista = await servico.ListarAsync(cenario.Admin.Id, CancellationToken.None);
            Assert.Equal(2, lista.Count(d => d.Origem == "instrumento"));
            Assert.All(lista.Where(d => d.Origem == "instrumento"), d => Assert.NotNull(d.RevogadoEm));
            Assert.Empty(await servico.ListarAsync(cenario.Comum.Id, CancellationToken.None));

            var comprovante = await servico.ComprovanteAsync(cenario.Admin.Id, "instrumento", a1[1].Id, CancellationToken.None);
            Assert.NotNull(comprovante);
            Assert.Equal("application/pdf", comprovante!.TipoConteudo);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(comprovante.Conteudo, 0, 4));
            Assert.Null(await servico.ComprovanteAsync(cenario.Comum.Id, "instrumento", a1[1].Id, CancellationToken.None));
        }
        finally
        {
            await LimparAsync(db, cenario);
        }
    }

    private sealed record Cenario(Tenant Conta, ApplicationUser Admin, ApplicationUser Comum, Empresa ComCertificado, Empresa NoAutomatico, string Pasta, string ImpressaoDigital);

    private static AceiteInstrumentosService Servico(IServiceProvider sp, AppDbContext db, string pasta, DateOnly hoje) =>
        new(db, sp.GetRequiredService<ICurrentTenant>(), sp.GetRequiredService<ICurrentUser>(), sp.GetRequiredService<IAuditLogWriter>(),
            sp.GetRequiredService<IHttpContextAccessor>(),
            new CertificateFileStore(_protecao, Options.Create(new CertificateStorageOptions { BasePath = pasta })),
            new MemoryCache(new MemoryCacheOptions()))
        {
            Hoje = () => hoje
        };

    private static readonly IDataProtectionProvider _protecao = new EphemeralDataProtectionProvider();

    private static async Task<Cenario> CriarCenarioAsync(IServiceProvider sp, AppDbContext db)
    {
        var s = Guid.NewGuid().ToString("N");
        var pasta = Path.Combine(Path.GetTempPath(), "jur1-cert-" + s);
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Aceites " + s, Perfil = PerfilConta.Contador };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        var grupoAdmin = new Grupo { Nome = "Admin " + s, EhAdministrador = true };
        var grupoComum = new Grupo { Nome = "Comum " + s };
        db.Grupos.AddRange(grupoAdmin, grupoComum);
        await db.SaveChangesAsync();
        var admin = new ApplicationUser { TenantId = conta.Id, UserName = $"adm{s}@t.com", Email = $"adm{s}@t.com", GrupoId = grupoAdmin.Id };
        var comum = new ApplicationUser { TenantId = conta.Id, UserName = $"com{s}@t.com", Email = $"com{s}@t.com", GrupoId = grupoComum.Id };
        db.Users.AddRange(admin, comum);

        var comCertificado = NovaEmpresa(s, "04747304000178", "Com certificado");
        var noAutomatico = NovaEmpresa(s, "11222333000181", "Automática");
        db.Empresas.AddRange(comCertificado, noAutomatico);
        await db.SaveChangesAsync();
        db.ConfiguracoesAutomacaoNotaMensal.Add(new ConfiguracaoAutomacaoNotaMensal
        {
            EmpresaId = noAutomatico.Id, Ativo = true, Modo = ModoAutomacaoNotaMensal.Automatico,
            Frequencia = FrequenciaAutomacaoNotaMensal.Mensal, DiaDoMes = 5, Horario = new TimeOnly(9, 0)
        });
        await db.SaveChangesAsync();

        using var rsa = RSA.Create(2048);
        var pedido = new CertificateRequest("CN=Certificado JUR1", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificado = pedido.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(30));
        var store = new CertificateFileStore(_protecao, Options.Create(new CertificateStorageOptions { BasePath = pasta }));
        await store.SalvarAsync(comCertificado.Id, certificado.Export(X509ContentType.Pfx, SenhaPfx), SenhaPfx, CancellationToken.None);

        return new Cenario(conta, admin, comum, comCertificado, noAutomatico, pasta, certificado.Thumbprint);
    }

    private static Empresa NovaEmpresa(string s, string cnpj, string nome) => new()
    {
        Cnpj = cnpj, RazaoSocial = nome + " " + s, InscricaoMunicipal = "1", CodigoMunicipio = "2919207", Uf = "BA",
        Cep = "42700000", Logradouro = "Rua", Numero = "1", Bairro = "Centro", Telefone = "71999999999", Email = $"e{s[..8]}@teste.com",
        OpSimpNac = "3", RegApTribSN = "1", RegEspTrib = "0", TribIssqn = "1", CstPisCofins = "00", PercentualTotalTributosSimplesNacional = "6.00"
    };

    private static async Task LimparAsync(AppDbContext db, Cenario c)
    {
        var id = c.Conta.Id;
        await db.AceitesInstrumentos.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync();
        await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync();
        await db.ConfiguracoesAutomacaoNotaMensal.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync();
        await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync();
        await db.Users.Where(x => x.TenantId == id).ExecuteDeleteAsync();
        await db.Grupos.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync();
        await db.Tenants.Where(x => x.Id == id).ExecuteDeleteAsync();
        if (Directory.Exists(c.Pasta))
            Directory.Delete(c.Pasta, recursive: true);
    }
}
