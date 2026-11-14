using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.UseCases.Seguranca;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// B3 pela API: ligar com o código do e-mail, login pedindo o código,
/// código errado recusado, certo entra; desligar pede a senha. O código é
/// gerado no teste pelo mesmo provedor do Identity (TOTP do SecurityStamp:
/// é o mesmo que foi para o e-mail). Desde o SEG1, administrador não
/// desliga (é obrigatória): o desligar é testado fora do grupo administrador.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class DuploFatorTests
{
    private const string Senha = "Senha@Forte123";
    private readonly AplicacaoTesteFactory _factory;

    public DuploFatorTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Ligar_entrar_com_codigo_e_desligar()
    {
        var cnpj = "3" + Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999).ToString();
        var email = $"duplo{cnpj}@teste.com";
        var cliente = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        // IP só deste teste: as várias chamadas de login não gastam o limite dos outros testes.
        cliente.DefaultRequestHeaders.Add(IpDoTesteStartupFilter.Cabecalho, $"10.9.{Random.Shared.Next(256)}.{Random.Shared.Next(1, 255)}");

        await AtualizarCsrfAsync(cliente, "/Account/Registrar");
        var registro = await cliente.PostAsJsonAsync("/api/auth/registrar", new
        {
            perfil = 2, documento = GerarCpf(), nome = "Duplo Fator", email, senha = Senha, aceitouTermos = true
        });
        Assert.Equal(HttpStatusCode.OK, registro.StatusCode);
        await AtualizarCsrfAsync(cliente, "/api/auth/situacao");

        Guid tenantId;
        using (var escopo = _factory.Services.CreateScope())
            tenantId = (await escopo.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.TenantId;

        try
        {
            // Ligar: código errado não liga; o certo liga.
            Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PostAsJsonAsync("/api/auth/duplo-fator/ativar", new { codigo = "000000" })).StatusCode);
            var codigoAtivacao = await GerarAsync(email, (um, u) => um.GenerateUserTokenAsync(u, RegrasDuploFator.Provedor, RegrasDuploFator.PropositoAtivacao));
            var ativar = await cliente.PostAsJsonAsync("/api/auth/duplo-fator/ativar", new { codigo = codigoAtivacao });
            Assert.Equal(HttpStatusCode.OK, ativar.StatusCode);

            var situacao = await cliente.GetFromJsonAsync<JsonElement>("/api/auth/duplo-fator");
            Assert.True(situacao.GetProperty("ligado").GetBoolean());
            Assert.True(situacao.GetProperty("emailConfirmado").GetBoolean());

            // Sai e entra de novo: a senha certa pede o código.
            await cliente.PostAsync("/api/auth/logout", null);
            await AtualizarCsrfAsync(cliente, "/Account/Login");
            var login = await cliente.PostAsJsonAsync("/api/auth/login", new { email, senha = Senha });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var corpo = await login.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(corpo.GetProperty("requerCodigo").GetBoolean());

            // Ainda não entrou.
            Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/api/auth/duplo-fator")).StatusCode);

            Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PostAsJsonAsync("/api/auth/login-codigo", new { codigo = "000000" })).StatusCode);
            var codigoLogin = await GerarAsync(email, (um, u) => um.GenerateTwoFactorTokenAsync(u, RegrasDuploFator.Provedor));
            var etapa2 = await cliente.PostAsJsonAsync("/api/auth/login-codigo", new { codigo = codigoLogin[..3] + " " + codigoLogin[3..] });
            Assert.Equal(HttpStatusCode.OK, etapa2.StatusCode);

            // Entrou. Administrador (SEG1): a verificação é obrigatória, não desliga.
            await AtualizarCsrfAsync(cliente, "/api/auth/situacao");
            var situacaoAdmin = await cliente.GetFromJsonAsync<JsonElement>("/api/auth/duplo-fator");
            Assert.True(situacaoAdmin.GetProperty("obrigatoria").GetBoolean());
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await cliente.PostAsJsonAsync("/api/auth/duplo-fator/desligar", new { senha = Senha })).StatusCode);

            // Fora do grupo administrador, desligar pede a senha.
            using (var escopoGrupo = _factory.Services.CreateScope())
            {
                var dbGrupo = escopoGrupo.ServiceProvider.GetRequiredService<AppDbContext>();
                var grupoComum = await dbGrupo.Grupos.IgnoreQueryFilters().Where(g => g.TenantId == tenantId && !g.EhAdministrador).Select(g => g.Id).FirstAsync();
                await dbGrupo.Users.Where(u => u.Email == email).ExecuteUpdateAsync(u => u.SetProperty(x => x.GrupoId, grupoComum));
            }
            Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PostAsJsonAsync("/api/auth/duplo-fator/desligar", new { senha = "errada" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsJsonAsync("/api/auth/duplo-fator/desligar", new { senha = Senha })).StatusCode);

            using var escopo = _factory.Services.CreateScope();
            var usuario = await escopo.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
            Assert.False(usuario!.TwoFactorEnabled);
            var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
            var auditoria = await db.AuditLogs.IgnoreQueryFilters().Where(a => a.TenantId == tenantId).Select(a => a.Operacao).ToListAsync();
            Assert.Contains("LigarDuploFator", auditoria);
            Assert.Contains("DesligarDuploFator", auditoria);
        }
        finally
        {
            await LimparAsync(tenantId);
        }
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
