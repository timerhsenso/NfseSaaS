using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Common;
using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Legal;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Legal;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// L8 — Termos e Política 1.1: o texto bate com as regras do código, a versão
/// 1.0 continua publicada, o aceite antigo vale até a data da nova versão e o
/// aviso prévio por e-mail sai uma vez por usuário.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class TermosVersao11Tests
{
    private readonly AplicacaoTesteFactory _factory;

    public TermosVersao11Tests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private async Task<string> PaginaAsync(string url)
    {
        var cliente = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resposta = await cliente.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return WebUtility.HtmlDecode(await resposta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Termos_1_1_trazem_o_responsavel_e_as_regras_com_os_numeros_do_codigo()
    {
        // JUR1: a 1.1 tem endereço próprio; /termos mostra a mais nova publicada.
        var termos = await PaginaAsync("/termos/1.1");
        Assert.Contains("Versão 1.1", termos);
        Assert.Contains(DocumentosLegais.ResponsavelNome, termos);
        Assert.Contains(DocumentosLegais.ResponsavelCnpj, termos);
        Assert.Contains($"{Planos.TesteDias} dias de teste grátis", termos);
        Assert.Contains($"até {Planos.DiasReservaFundadorAposTeste} dias depois do fim do teste grátis", termos);
        Assert.Contains($"por mais <strong>{Planos.ToleranciaAtrasoDias} dias</strong> depois do vencimento", termos);
        Assert.Contains("só a emissão em produção fica bloqueada", termos);
        Assert.Contains("Atraso de pagamento não suspende a Conta", termos);
        Assert.Contains("cancelar a qualquer momento pela tela Assinatura", termos);
        Assert.Contains("Cancelando a assinatura, o preço de fundador se perde", termos);
        Assert.Contains("1 (um) mês grátis", termos);
        Assert.Contains("chargeback", termos);
        Assert.Contains("/termos/1.0", termos);
        Assert.Contains($"Versão {DocumentosLegais.Atual(DataBrasilia.Hoje()).Versao}", await PaginaAsync("/termos"));

        var politica = await PaginaAsync("/privacidade/1.1");
        Assert.Contains("Versão 1.1", politica);
        Assert.Contains(DocumentosLegais.ResponsavelNome, politica);
        Assert.Contains("Asaas", politica);
        Assert.Contains("Registro do teste grátis", politica);
        Assert.Contains("e-mail das pessoas convidadas", politica);

        // Venda pela internet: razão social e CNPJ no rodapé do site (Decreto 7.962/2013).
        var inicio = await PaginaAsync("/precos");
        Assert.Contains($"CNPJ {DocumentosLegais.ResponsavelCnpj}", inicio);
    }

    [Fact]
    public async Task Versao_1_0_continua_publicada_como_foi_aceita()
    {
        var termos = await PaginaAsync("/termos/1.0");
        Assert.Contains("Versão 1.0 · vigente de 04/11/2026", termos);
        Assert.Contains("Carlos Eduardo Caetano Santos", termos);
        Assert.Contains("/privacidade/1.0", termos);
        Assert.DoesNotContain("LFMS - Consultoria", termos[..termos.IndexOf("<footer", StringComparison.Ordinal)]);

        var politica = await PaginaAsync("/privacidade/1.0");
        Assert.Contains("Versão 1.0 · vigente de 04/11/2026", politica);
        Assert.DoesNotContain("LFMS - Consultoria", politica[..politica.IndexOf("<footer", StringComparison.Ordinal)]);
    }

    [Fact]
    public async Task Aceite_da_1_0_vale_ate_a_data_da_1_1_e_aceite_antecipado_da_1_1_vale_sempre()
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
        var (tenant, usuario) = await CriarUsuarioAsync(db);
        try
        {
            db.TenantIdOverrideDeSistema = tenant.Id;
            db.AceitesTermos.Add(new AceiteTermos
            {
                UsuarioId = usuario.Id, VersaoTermosDeUso = "1.0",
                VersaoPoliticaPrivacidade = "1.0", Origem = OrigemAceiteTermos.Cadastro
            });
            await db.SaveChangesAsync();

            using (var e1 = _factory.Services.CreateScope())
            {
                // Como numa requisição logada: o Tenant vem do login.
                e1.ServiceProvider.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(tenant.Id);
                var aceite = e1.ServiceProvider.GetRequiredService<IAceiteTermosService>();
                Assert.False(await aceite.AceitouVersaoAtualAsync(usuario.Id, CancellationToken.None));
                // Antes de 24/11/2026 o aceite da 1.0 libera; depois, não.
                Assert.Equal(DocumentosLegais.AceiteLibera("1.0", DataBrasilia.Hoje()),
                    await aceite.AceitouVersaoVigenteAsync(usuario.Id, CancellationToken.None));
            }

            db.AceitesTermos.Add(new AceiteTermos
            {
                UsuarioId = usuario.Id, VersaoTermosDeUso = DocumentosLegais.Atual(DataBrasilia.Hoje()).Versao,
                VersaoPoliticaPrivacidade = DocumentosLegais.Atual(DataBrasilia.Hoje()).Versao, Origem = OrigemAceiteTermos.Atualizacao
            });
            await db.SaveChangesAsync();

            using (var e2 = _factory.Services.CreateScope())
            {
                e2.ServiceProvider.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(tenant.Id);
                var aceite = e2.ServiceProvider.GetRequiredService<IAceiteTermosService>();
                Assert.True(await aceite.AceitouVersaoAtualAsync(usuario.Id, CancellationToken.None));
                Assert.True(await aceite.AceitouVersaoVigenteAsync(usuario.Id, CancellationToken.None));
            }
        }
        finally
        {
            db.TenantIdOverrideDeSistema = null;
            await LimparAsync(db, tenant.Id);
        }
    }

    [Theory]
    [InlineData("/termos/1.2")]
    [InlineData("/privacidade/1.2")]
    [InlineData("/dpa")]
    [InlineData("/autorizacao-certificado/1.0")]
    [InlineData("/autorizacao-emissao-automatica")]
    public async Task Versao_1_2_e_instrumentos_so_aparecem_depois_de_publicados(string url)
    {
        // JUR1: a 1.2 é publicada em 24/11/2026; antes disso o endereço não existe.
        var publicada = DocumentosLegais.Publicada("1.2", DataBrasilia.Hoje()) is not null;
        var cliente = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resposta = await cliente.GetAsync(url);
        Assert.Equal(publicada ? HttpStatusCode.OK : HttpStatusCode.NotFound, resposta.StatusCode);
        if (publicada && !url.Contains("1.2"))
            Assert.Contains("Identificação deste texto (SHA-256)", WebUtility.HtmlDecode(await resposta.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Aviso_previo_sai_uma_vez_por_usuario_e_nao_vai_para_quem_ja_aceitou()
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
        var (tenant, usuario) = await CriarUsuarioAsync(db);
        var (_, naoConfirmado) = await CriarUsuarioAsync(db, tenant, emailConfirmado: false);
        try
        {
            using (var e1 = _factory.Services.CreateScope())
            {
                e1.ServiceProvider.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(tenant.Id);
                Assert.Equal(1, await e1.ServiceProvider.GetRequiredService<AvisoNovosTermosService>().AvisarContaAsync(CancellationToken.None));
            }
            using (var e2 = _factory.Services.CreateScope())
            {
                e2.ServiceProvider.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(tenant.Id);
                Assert.Equal(0, await e2.ServiceProvider.GetRequiredService<AvisoNovosTermosService>().AvisarContaAsync(CancellationToken.None));
            }

            var emails = await db.EmailLogs.IgnoreQueryFilters().AsNoTracking()
                .Where(e => e.TenantId == tenant.Id && e.Tipo == TipoEmailCatalogo.NovosTermos).ToListAsync();
            var email = Assert.Single(emails);
            Assert.Equal(usuario.Email, email.Destinatario);
            Assert.Contains(DocumentosLegais.Atual(DataBrasilia.Hoje()).VigenteDesde.ToString("dd/MM/yyyy"), email.Assunto);
            Assert.Contains("/termos", email.CorpoHtml);
            Assert.DoesNotContain(emails, e => e.Destinatario == naoConfirmado.Email);
        }
        finally
        {
            await LimparAsync(db, tenant.Id);
        }
    }

    private static async Task<(Tenant Tenant, ApplicationUser Usuario)> CriarUsuarioAsync(AppDbContext db, Tenant? tenant = null, bool emailConfirmado = true)
    {
        var s = Guid.NewGuid().ToString("N");
        if (tenant is null)
        {
            tenant = new Tenant { Documento = s[..14], RazaoSocial = "Termos " + s };
            db.Tenants.Add(tenant);
        }
        var email = $"termos{s}@teste.com";
        var usuario = new ApplicationUser
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = emailConfirmado,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(usuario);
        await db.SaveChangesAsync();
        return (tenant, usuario);
    }

    private static async Task LimparAsync(AppDbContext db, Guid tenantId)
    {
        await db.EmailLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.AceitesTermos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Tenants.Where(x => x.Id == tenantId).ExecuteDeleteAsync();
    }
}
