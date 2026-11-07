using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Rejeicoes;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// Site público (sem login): página inicial, erros da NFS-e explicados,
/// sitemap e robots. Nada disso pode exigir login nem aceite de termos.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class SitePublicoTests
{
    private readonly AplicacaoTesteFactory _factory;

    public SitePublicoTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private HttpClient Cliente() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Inicio_sem_login_mostra_a_pagina_publica()
    {
        var resposta = await Cliente().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var html = await resposta.Content.ReadAsStringAsync();
        Assert.Contains("Criar conta grátis", html);
        // Contato do suporte (appsettings "Suporte") no rodapé; WhatsApp vazio não aparece.
        Assert.Contains("mailto:suporte@micoempresa.com.br", html);
        Assert.DoesNotContain("wa.me/", html);

        // Tela de login (layout de autenticação): contato para as mensagens de erro.
        var login = await Cliente().GetStringAsync("/Account/Login");
        Assert.Contains("data-suporte=\"suporte@micoempresa.com.br\"", login);
        Assert.Contains("/Account/Registrar", html);
    }

    [Fact]
    public async Task Precos_vem_do_catalogo_com_planos_de_empresa_e_de_escritorio()
    {
        using var escopo = _factory.Services.CreateScope();
        var catalogo = escopo.ServiceProvider.GetRequiredService<NfseSaaS.Application.UseCases.Cobranca.ICatalogoPlanos>();
        var empresa = await catalogo.PlanosAVendaAsync(NfseSaaS.Domain.Enums.PerfilConta.Empresa, CancellationToken.None);
        var contador = await catalogo.PlanosAVendaAsync(NfseSaaS.Domain.Enums.PerfilConta.Contador, CancellationToken.None);

        var html = System.Net.WebUtility.HtmlDecode(await (await Cliente().GetComStatus200Async("/precos")).Content.ReadAsStringAsync());
        Assert.Contains("30 dias grátis, sem cartão.", html);
        Assert.Contains("a primeira cobrança só vem no fim do teste", html);
        Assert.Contains("<link rel=\"canonical\" href=\"http://localhost/precos\"", html);
        Assert.Contains("Portal do Emissor Nacional", html);
        foreach (var plano in empresa.Concat(contador).Where(p => p.Publico == NfseSaaS.Domain.Cobranca.PublicoPlano.Escritorios || p.OfertarParaEmpresa))
            Assert.Contains($"<h3>{plano.Nome}</h3>", html);

        // A página inicial mostra só os planos de empresa e aponta para /precos.
        var inicio = System.Net.WebUtility.HtmlDecode(await (await Cliente().GetComStatus200Async("/")).Content.ReadAsStringAsync());
        Assert.Contains("href=\"/precos\"", inicio);
        foreach (var plano in empresa.Where(p => p.Publico == NfseSaaS.Domain.Cobranca.PublicoPlano.Empresas))
            Assert.Contains($"<h3>{plano.Nome}</h3>", inicio);
        Assert.DoesNotContain("id=\"escritorios\"", inicio);
    }

    [Fact]
    public async Task Ajuda_do_certificado_e_publica()
    {
        var html = await (await Cliente().GetComStatus200Async("/ajuda/certificado-digital-a1")).Content.ReadAsStringAsync();
        Assert.Contains("Exportar", html);
        Assert.Contains("A3", html);
    }

    [Fact]
    public async Task Link_de_indicacao_chega_ao_cadastro_e_valor_invalido_e_descartado()
    {
        var comCodigo = await (await Cliente().GetComStatus200Async("/?ref=k7m2qx9a&origem=email-nota")).Content.ReadAsStringAsync();
        Assert.Contains("/Account/Registrar?ref=K7M2QX9A&amp;origem=email-nota", comCodigo);

        var invalido = await (await Cliente().GetComStatus200Async("/?ref=%3Cscript%3E")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Registrar?ref=", invalido);
    }

    [Fact]
    public async Task Indice_de_erros_lista_todos_os_codigos()
    {
        var html = await (await Cliente().GetComStatus200Async("/erros-nfse")).Content.ReadAsStringAsync();

        foreach (var codigo in CatalogoRejeicoesSefin.Codigos)
            Assert.Contains($"/erros-nfse/{codigo}\"", html);
    }

    [Fact]
    public async Task Pagina_do_erro_explica_o_codigo()
    {
        var codigo = CatalogoRejeicoesSefin.Todas[0].Codigo;

        var resposta = await Cliente().GetAsync($"/erros-nfse/{codigo}");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var html = await resposta.Content.ReadAsStringAsync();
        Assert.Contains("Como resolver", html);
        Assert.Contains($"<link rel=\"canonical\" href=\"http://localhost/erros-nfse/{codigo}\"", html);
    }

    [Fact]
    public async Task Codigo_em_minusculas_redireciona_para_o_endereco_oficial()
    {
        var codigo = CatalogoRejeicoesSefin.Todas[0].Codigo;

        var resposta = await Cliente().GetAsync($"/erros-nfse/{codigo.ToLowerInvariant()}");

        Assert.Equal(HttpStatusCode.MovedPermanently, resposta.StatusCode);
        Assert.Equal($"/erros-nfse/{codigo}", resposta.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Codigo_inexistente_responde_404_com_pagina_amigavel()
    {
        var resposta = await Cliente().GetAsync("/erros-nfse/E9999");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Contains("Não encontramos o código", await resposta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sitemap_e_robots_apontam_para_as_paginas_publicas()
    {
        var sitemap = await (await Cliente().GetComStatus200Async("/sitemap.xml")).Content.ReadAsStringAsync();
        Assert.Contains("<loc>http://localhost/erros-nfse</loc>", sitemap);
        Assert.Contains("<loc>http://localhost/precos</loc>", sitemap);
        Assert.Equal(CatalogoRejeicoesSefin.Quantidade + NfseSaaS.Web.Controllers.SiteController.PaginasFixas.Length, sitemap.Split("<loc>").Length - 1);

        var robots = await (await Cliente().GetComStatus200Async("/robots.txt")).Content.ReadAsStringAsync();
        Assert.Contains("Sitemap: http://localhost/sitemap.xml", robots);
        Assert.Contains("Disallow: /api/", robots);
        Assert.Contains("Allow: /\n", robots);
        Assert.DoesNotContain("Disallow: /\n", robots); // nunca fechar o site inteiro (bloqueava o próprio sitemap)
    }
}

internal static class HttpClientTesteExtensions
{
    /// <summary>GET que falha o teste se a resposta não for 200.</summary>
    public static async Task<HttpResponseMessage> GetComStatus200Async(this HttpClient cliente, string url)
    {
        var resposta = await cliente.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return resposta;
    }
}

[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class OnboardingApiTests
{
    private readonly AplicacaoTesteFactory _factory;

    public OnboardingApiTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Indicacao_exige_login()
    {
        var resposta = await _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .GetAsync("/api/indicacao");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Primeiros_passos_exige_login()
    {
        var resposta = await _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .GetAsync("/api/onboarding");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }
}
