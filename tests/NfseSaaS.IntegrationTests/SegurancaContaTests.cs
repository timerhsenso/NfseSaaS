using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.UseCases.Seguranca;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Legal;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Web.Services;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// SEG1 pela API:
/// - administrador dentro do prazo usa o sistema e vê a verificação como obrigatória;
/// - operação sensível sem senha recente → 403 "confirmar_senha"; a senha libera;
/// - exigir de todos pede a própria verificação ligada;
/// - usuário comum de conta que exige, com o prazo vencido, só abre a tela de
///   ligar (API 403 "duplo_fator_pendente", tela redireciona); ligou, entra.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class SegurancaContaTests
{
    private const string Senha = "Senha@Forte123";
    private readonly AplicacaoTesteFactory _factory;

    public SegurancaContaTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Exigencia_confirmacao_de_senha_e_bloqueio_depois_do_prazo()
    {
        var sufixo = Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString();
        var emailAdmin = $"seg-admin{sufixo}@teste.com";
        var emailComum = $"seg-comum{sufixo}@teste.com";

        var cookiesAdmin = new CookieContainer();
        var admin = NovoCliente(cookiesAdmin);

        await AtualizarCsrfAsync(admin, "/Account/Registrar");
        var registro = await admin.PostAsJsonAsync("/api/auth/registrar", new
        {
            perfil = 2, documento = GerarCpf(), nome = "Seguranca Conta", email = emailAdmin, senha = Senha, aceitouTermos = true
        });
        Assert.Equal(HttpStatusCode.OK, registro.StatusCode);
        await AtualizarCsrfAsync(admin, "/api/auth/situacao");

        Guid tenantId;
        using (var escopo = _factory.Services.CreateScope())
            tenantId = (await escopo.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(emailAdmin))!.TenantId;

        try
        {
            // Administrador novo: obrigatória, mas com prazo — o sistema abre.
            var situacao = await admin.GetFromJsonAsync<JsonElement>("/api/auth/duplo-fator");
            Assert.True(situacao.GetProperty("obrigatoria").GetBoolean());
            var prazoEsperado = (DataBrasilia.Hoje() > RegrasDuploFator.ObrigatoriaParaAdministradoresDesde
                ? DataBrasilia.Hoje() : RegrasDuploFator.ObrigatoriaParaAdministradoresDesde).AddDays(RegrasDuploFator.DiasPrazoAdesao);
            Assert.Equal(prazoEsperado.ToString("yyyy-MM-dd"), situacao.GetProperty("prazo").GetString());
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/conta/dados")).StatusCode);
            var painel = await admin.GetAsync("/");
            Assert.Contains("aviso-duplo-fator", await painel.Content.ReadAsStringAsync());

            // Seção Segurança: lê.
            var seguranca = await admin.GetFromJsonAsync<JsonElement>("/api/conta/seguranca");
            Assert.False(seguranca.GetProperty("exigirDeTodos").GetBoolean());
            Assert.False(seguranca.GetProperty("voceLigou").GetBoolean());

            // Sem senha recente (o cadastro marcou; aqui a marca some): 403 "confirmar_senha".
            cookiesAdmin.Add(new Uri("http://localhost"), new Cookie(ConfirmacaoSenhaRecente.NomeCookie, "") { Expired = true });
            var semSenha = await admin.PutAsJsonAsync("/api/conta/seguranca", new { exigirDeTodos = true });
            Assert.Equal(HttpStatusCode.Forbidden, semSenha.StatusCode);
            Assert.Equal(ConfirmacaoSenhaRecente.CodigoExigida, (await semSenha.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString());

            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/auth/confirmar-senha", new { senha = "errada" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/auth/confirmar-senha", new { senha = Senha })).StatusCode);

            // Senha confirmada; exigir de todos pede a própria ligada.
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PutAsJsonAsync("/api/conta/seguranca", new { exigirDeTodos = true })).StatusCode);

            var codigoAtivacao = await GerarAsync(emailAdmin, (um, u) => um.GenerateUserTokenAsync(u, RegrasDuploFator.Provedor, RegrasDuploFator.PropositoAtivacao));
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/auth/duplo-fator/ativar", new { codigo = codigoAtivacao })).StatusCode);
            Assert.DoesNotContain("aviso-duplo-fator", await (await admin.GetAsync("/")).Content.ReadAsStringAsync());

            var exigida = await admin.PutAsJsonAsync("/api/conta/seguranca", new { exigirDeTodos = true });
            Assert.Equal(HttpStatusCode.OK, exigida.StatusCode);
            Assert.True((await exigida.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("exigirDeTodos").GetBoolean());

            // Usuário comum que entrou há 30 dias, conta exigindo há 30 dias: prazo vencido.
            await CriarUsuarioComumAsync(tenantId, emailComum, DateTimeOffset.UtcNow.AddDays(-30));

            var comum = NovoCliente(new CookieContainer());
            await AtualizarCsrfAsync(comum, "/Account/Login");
            Assert.Equal(HttpStatusCode.OK, (await comum.PostAsJsonAsync("/api/auth/login", new { email = emailComum, senha = Senha })).StatusCode);
            await AtualizarCsrfAsync(comum, "/api/auth/situacao");

            var barrado = await comum.GetAsync("/api/conta/dados");
            Assert.Equal(HttpStatusCode.Forbidden, barrado.StatusCode);
            var corpoBarrado = await barrado.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(RegrasDuploFator.CodigoPendente, corpoBarrado.GetProperty("codigo").GetString());

            var tela = await comum.GetAsync("/Clientes");
            Assert.Equal(HttpStatusCode.Redirect, tela.StatusCode);
            Assert.Equal($"{RegrasDuploFator.CaminhoTelaObrigatoria}?returnUrl=%2FClientes", tela.Headers.Location!.OriginalString);
            Assert.Equal(HttpStatusCode.OK, (await comum.GetAsync(RegrasDuploFator.CaminhoTelaObrigatoria)).StatusCode);

            // A tela de ligar funciona com o bloqueio; ligou, entra.
            var codigoComum = await GerarAsync(emailComum, (um, u) => um.GenerateUserTokenAsync(u, RegrasDuploFator.Provedor, RegrasDuploFator.PropositoAtivacao));
            Assert.Equal(HttpStatusCode.OK, (await comum.PostAsJsonAsync("/api/auth/duplo-fator/ativar", new { codigo = codigoComum })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await comum.GetAsync("/api/conta/dados")).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await comum.GetAsync(RegrasDuploFator.CaminhoTelaObrigatoria)).StatusCode);

            // Exigida pela conta: não desliga.
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await comum.PostAsJsonAsync("/api/auth/duplo-fator/desligar", new { senha = Senha })).StatusCode);

            // Usuário comum não mexe na segurança da conta.
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await comum.GetAsync("/api/conta/seguranca")).StatusCode);

            using var escopoFim = _factory.Services.CreateScope();
            var db = escopoFim.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Contains("AlterarExigenciaDuploFator", await db.AuditLogs.IgnoreQueryFilters().Where(a => a.TenantId == tenantId).Select(a => a.Operacao).ToListAsync());
        }
        finally
        {
            await LimparAsync(tenantId);
        }
    }

    private HttpClient NovoCliente(CookieContainer cookies)
    {
        var cliente = _factory.CreateDefaultClient(new CookieContainerHandler(cookies));
        // IP só deste cliente: os logins não gastam o limite dos outros testes.
        cliente.DefaultRequestHeaders.Add(IpDoTesteStartupFilter.Cabecalho, $"10.8.{Random.Shared.Next(256)}.{Random.Shared.Next(1, 255)}");
        return cliente;
    }

    private async Task CriarUsuarioComumAsync(Guid tenantId, string email, DateTimeOffset entrouEm)
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
        var um = escopo.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var grupoComum = await db.Grupos.IgnoreQueryFilters().Where(g => g.TenantId == tenantId && !g.EhAdministrador).Select(g => g.Id).FirstAsync();
        var usuario = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, TenantId = tenantId, GrupoId = grupoComum };
        var criado = await um.CreateAsync(usuario, Senha);
        Assert.True(criado.Succeeded, string.Join(" ", criado.Errors.Select(e => e.Description)));

        db.TenantIdOverrideDeSistema = tenantId;
        db.AceitesTermos.Add(new AceiteTermos
        {
            TenantId = tenantId,
            UsuarioId = usuario.Id,
            VersaoTermosDeUso = DocumentosLegais.VersaoTermosDeUso,
            VersaoPoliticaPrivacidade = DocumentosLegais.VersaoPoliticaPrivacidade,
            AceitoEm = entrouEm,
            Origem = OrigemAceiteTermos.Convite
        });
        await db.SaveChangesAsync();
        db.TenantIdOverrideDeSistema = null;

        await db.Tenants.Where(t => t.Id == tenantId).ExecuteUpdateAsync(t => t.SetProperty(x => x.DuploFatorObrigatorioDesde, entrouEm));
    }

    private async Task<string> GerarAsync(string email, Func<UserManager<ApplicationUser>, ApplicationUser, Task<string>> gerar)
    {
        using var escopo = _factory.Services.CreateScope();
        var um = escopo.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await gerar(um, (await um.FindByEmailAsync(email))!);
    }

    private async Task LimparAsync(Guid tenantId)
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.AceitesTermos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.EmailLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.GrupoTelas.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Grupos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Assinaturas.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Tenants.Where(x => x.Id == tenantId).ExecuteDeleteAsync();
    }

    private static async Task AtualizarCsrfAsync(HttpClient cliente, string url)
    {
        var resposta = await cliente.GetAsync(url);
        var cookie = resposta.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("NfseSaaS.Xsrf-Token="));
        var token = Uri.UnescapeDataString(cookie.Split(';')[0]["NfseSaaS.Xsrf-Token=".Length..]);
        cliente.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        cliente.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
    }

    /// <summary>CPF aleatório com dígitos verificadores certos (conta de contador, sem consulta de CNPJ).</summary>
    private static string GerarCpf()
    {
        var d = Enumerable.Range(0, 9).Select(_ => Random.Shared.Next(10)).ToList();
        for (var n = 9; n < 11; n++)
        {
            var soma = d.Select((x, i) => x * (n + 1 - i)).Sum();
            var resto = soma * 10 % 11;
            d.Add(resto == 10 ? 0 : resto);
        }
        return string.Concat(d);
    }
}
