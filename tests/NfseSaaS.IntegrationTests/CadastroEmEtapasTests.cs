using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cadastro;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// Cadastro em etapas pela API de verdade (com CSRF): perfil Empresa já
/// nasce com a Empresa (dados da "Receita" falsa), perfil Contador com CPF
/// sem Empresa, CNPJ baixado recusado, consulta fora do ar não trava, e a
/// confirmação do e-mail liberando a produção.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class CadastroEmEtapasTests
{
    private readonly AplicacaoTesteFactory _factory;

    public CadastroEmEtapasTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    // ---------- Apoio ----------

    /// <summary>CNPJ válido (DV certo) começando com o prefixo, sem colisão entre execuções.</summary>
    private static string GerarCnpj(string prefixo)
    {
        var aleatorio = string.Concat(Enumerable.Range(0, 12 - prefixo.Length).Select(_ => Random.Shared.Next(10)));
        var baseCnpj = prefixo + aleatorio;
        int Digito(string b, int[] pesos)
        {
            var resto = b.Select((c, i) => (c - '0') * pesos[i]).Sum() % 11;
            return resto < 2 ? 0 : 11 - resto;
        }
        var d1 = Digito(baseCnpj, new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 });
        var d2 = Digito(baseCnpj + d1, new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 });
        return baseCnpj + d1 + d2;
    }

    private static string GerarCpf()
    {
        var baseCpf = Random.Shared.NextInt64(100_000_000, 999_999_999).ToString();
        int Digito(string b, int peso)
        {
            var resto = b.Select((c, i) => (c - '0') * (peso - i)).Sum() % 11;
            return resto < 2 ? 0 : 11 - resto;
        }
        var d1 = Digito(baseCpf, 10);
        var d2 = Digito(baseCpf + d1, 11);
        return baseCpf + d1 + d2;
    }

    /// <summary>Cliente com o cookie anti-CSRF já lido (o middleware o emite em toda resposta).</summary>
    private async Task<HttpClient> ClienteAsync()
    {
        var cliente = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var resposta = await cliente.GetAsync("/Account/Registrar");
        var cookie = resposta.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("NfseSaaS.Xsrf-Token="));
        var token = Uri.UnescapeDataString(cookie.Split(';')[0]["NfseSaaS.Xsrf-Token=".Length..]);
        cliente.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
        return cliente;
    }

    private static Task<HttpResponseMessage> RegistrarAsync(HttpClient cliente, int perfil, string documento, string nome, string email) =>
        cliente.PostAsJsonAsync("/api/auth/registrar", new
        {
            perfil, documento, nome, email, senha = "Senha@Forte123", aceitouTermos = true
        });

    private async Task LimparAsync(params string[] documentos)
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = await db.Tenants.Where(t => documentos.Contains(t.Documento)).Select(t => t.Id).ToListAsync();
        await db.EmailLogs.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.AceitesTermos.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.AuditLogs.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Empresas.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Users.Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.GrupoTelas.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Grupos.IgnoreQueryFilters().Where(x => ids.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Tenants.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync();
    }

    // ---------- Testes ----------

    [Fact]
    public async Task Consulta_publica_do_cnpj_mostra_so_nome_cidade_e_situacao()
    {
        var cliente = await ClienteAsync();
        var cnpj = GerarCnpj("1");

        var resposta = await cliente.GetFromJsonAsync<JsonElement>($"/api/cadastro/cnpj/{cnpj}");

        Assert.Equal($"EMPRESA TESTE {cnpj} LTDA", resposta.GetProperty("razaoSocial").GetString());
        Assert.Equal("SALVADOR", resposta.GetProperty("municipio").GetString());
        Assert.True(resposta.GetProperty("ativa").GetBoolean());
        Assert.False(resposta.GetProperty("jaTemConta").GetBoolean());
        // Sem dados de contato de terceiros na página pública.
        Assert.False(resposta.TryGetProperty("telefone", out _));
        Assert.False(resposta.TryGetProperty("email", out _));

        var invalido = await cliente.GetAsync("/api/cadastro/cnpj/11111111111111");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalido.StatusCode);
    }

    [Fact]
    public async Task Perfil_empresa_nasce_com_a_empresa_pendente_e_so_emite_em_producao_com_email_confirmado()
    {
        var cnpj = GerarCnpj("2");
        var email = $"empresa{cnpj}@teste.com";
        try
        {
            var cliente = await ClienteAsync();
            var resposta = await RegistrarAsync(cliente, 1, cnpj, "Nome digitado", email);
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

            using var escopo = _factory.Services.CreateScope();
            var sp = escopo.ServiceProvider;
            var db = sp.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Documento == cnpj);
            Assert.Equal(PerfilConta.Empresa, tenant.Perfil);

            // Empresa criada com os dados da Receita; IM e regime ficam pendentes.
            var empresa = await db.Empresas.IgnoreQueryFilters().AsNoTracking().SingleAsync(e => e.TenantId == tenant.Id);
            Assert.Equal(cnpj, empresa.Cnpj);
            Assert.Equal($"EMPRESA TESTE {cnpj} LTDA", empresa.RazaoSocial);
            Assert.Equal("2927408", empresa.CodigoMunicipio);
            Assert.Equal("7133445566", empresa.Telefone);
            Assert.Equal(email, empresa.Email); // o da conta, não o da Receita
            Assert.Equal("3", empresa.OpSimpNac); // sugestão: optante do Simples
            Assert.Equal(TipoAmbiente.Homologacao, empresa.TipoAmbiente);
            var pendencias = empresa.PendenciasCadastro();
            Assert.Contains("Inscrição Municipal", pendencias);
            Assert.Contains("dados fiscais (regime e tributação)", pendencias);

            // E-mail de confirmação na fila; a conta ainda não está confirmada.
            Assert.True(await db.EmailLogs.IgnoreQueryFilters().AnyAsync(e => e.TenantId == tenant.Id && e.Tipo == TipoEmailCatalogo.ConfirmacaoEmail));
            var situacao = await cliente.GetFromJsonAsync<JsonElement>("/api/auth/situacao");
            Assert.False(situacao.GetProperty("contaConfirmada").GetBoolean());

            // Primeiros passos: confirmar e-mail e completar os dados da empresa.
            var onboarding = await cliente.GetFromJsonAsync<JsonElement>("/api/onboarding");
            var passos = onboarding.GetProperty("passos").EnumerateArray().ToList();
            Assert.Equal("email", passos[0].GetProperty("chave").GetString());
            Assert.False(passos[0].GetProperty("concluido").GetBoolean());
            Assert.Equal("Complete os dados da empresa", passos[1].GetProperty("titulo").GetString());

            // Emissão: com a empresa pendente, a mensagem diz o que falta.
            sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(tenant.Id);
            var emitir = sp.GetRequiredService<IEmitirNfseUseCase>();
            var pedido = new EmitirNfseRequest(empresa.Id, Guid.NewGuid(), Guid.NewGuid(), 100m, "Teste", new DateOnly(2026, 11, 1));
            var pendente = await Assert.ThrowsAsync<RegraNegocioException>(() => emitir.ExecutarAsync(pedido, CancellationToken.None));
            Assert.Contains("Inscrição Municipal", pendente.Message);

            // Em produção, sem e-mail confirmado, nem chega a olhar o resto.
            await db.Empresas.IgnoreQueryFilters().Where(e => e.Id == empresa.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(e => e.TipoAmbiente, TipoAmbiente.Producao));
            var semEmail = await Assert.ThrowsAsync<RegraNegocioException>(() => emitir.ExecutarAsync(pedido, CancellationToken.None));
            Assert.Contains("Confirme o e-mail", semEmail.Message);

            // Confirmou pelo link: a trava do e-mail sai (volta a valer a de cadastro incompleto).
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var usuario = await userManager.FindByEmailAsync(email);
            var tokenConfirmacao = await userManager.GenerateEmailConfirmationTokenAsync(usuario!);
            Assert.False(await sp.GetRequiredService<IConfirmacaoEmailService>().ConfirmarAsync(usuario!.Id, "token-errado", CancellationToken.None));
            Assert.True(await sp.GetRequiredService<IConfirmacaoEmailService>().ConfirmarAsync(usuario.Id, tokenConfirmacao, CancellationToken.None));

            var depois = await Assert.ThrowsAsync<RegraNegocioException>(() => emitir.ExecutarAsync(pedido, CancellationToken.None));
            Assert.DoesNotContain("Confirme o e-mail", depois.Message);
            Assert.Contains("Inscrição Municipal", depois.Message);

            situacao = await cliente.GetFromJsonAsync<JsonElement>("/api/auth/situacao");
            Assert.True(situacao.GetProperty("contaConfirmada").GetBoolean());

            // Mesmo CNPJ de novo: conta já existe.
            var repetido = await RegistrarAsync(await ClienteAsync(), 1, cnpj, "Outra", $"outro{cnpj}@teste.com");
            Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        }
        finally
        {
            await LimparAsync(cnpj);
        }
    }

    [Fact]
    public async Task Contador_com_cpf_nao_ganha_empresa_e_ve_o_passo_de_cadastrar_cliente()
    {
        var cpf = GerarCpf();
        try
        {
            var cliente = await ClienteAsync();
            var resposta = await RegistrarAsync(cliente, 2, cpf, "Fulana Contadora", $"contadora{cpf}@teste.com");
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

            using var escopo = _factory.Services.CreateScope();
            var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Documento == cpf);
            Assert.Equal(PerfilConta.Contador, tenant.Perfil);
            Assert.Equal("Fulana Contadora", tenant.RazaoSocial);
            Assert.False(await db.Empresas.IgnoreQueryFilters().AnyAsync(e => e.TenantId == tenant.Id));

            var onboarding = await cliente.GetFromJsonAsync<JsonElement>("/api/onboarding");
            var passoEmpresa = onboarding.GetProperty("passos").EnumerateArray().Single(p => p.GetProperty("chave").GetString() == "empresa");
            Assert.Equal("Cadastre a primeira empresa cliente", passoEmpresa.GetProperty("titulo").GetString());

            // CPF no perfil Empresa não vale.
            var cpfComoEmpresa = await RegistrarAsync(await ClienteAsync(), 1, GerarCpf(), "X", $"x{cpf}@teste.com");
            Assert.Equal(HttpStatusCode.BadRequest, cpfComoEmpresa.StatusCode);
        }
        finally
        {
            await LimparAsync(cpf);
        }
    }

    [Fact]
    public async Task Cnpj_baixado_e_recusado_e_consulta_fora_do_ar_nao_trava_o_cadastro()
    {
        var baixado = GerarCnpj("99");
        var semConsulta = GerarCnpj("98");
        try
        {
            var recusado = await RegistrarAsync(await ClienteAsync(), 1, baixado, "Baixada", $"b{baixado}@teste.com");
            Assert.Equal(HttpStatusCode.BadRequest, recusado.StatusCode);
            Assert.Contains("BAIXADA", await recusado.Content.ReadAsStringAsync());

            var criado = await RegistrarAsync(await ClienteAsync(), 1, semConsulta, "Empresa Digitada Ltda", $"s{semConsulta}@teste.com");
            Assert.Equal(HttpStatusCode.OK, criado.StatusCode);

            using var escopo = _factory.Services.CreateScope();
            var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
            var empresa = await db.Empresas.IgnoreQueryFilters().AsNoTracking().SingleAsync(e => e.Cnpj == semConsulta);
            Assert.Equal("Empresa Digitada Ltda", empresa.RazaoSocial);
            Assert.Contains("endereço completo", empresa.PendenciasCadastro());
        }
        finally
        {
            await LimparAsync(baixado, semConsulta);
        }
    }
}
