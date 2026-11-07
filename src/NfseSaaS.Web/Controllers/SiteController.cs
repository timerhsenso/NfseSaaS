using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Rejeicoes;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>
/// Páginas públicas do site (sem login): a explicação de cada rejeição da
/// SEFIN em /erros-nfse/{codigo}, o índice /erros-nfse, o sitemap.xml e o
/// robots.txt. A página inicial pública fica no HomeController (mesma rota
/// "/", que mostra o painel para quem está logado).
///
/// Objetivo: quem recebe uma rejeição no portal do governo procura o código
/// no Google e encontra a explicação aqui. Conteúdo vem do
/// CatalogoRejeicoesSefin (anexos oficiais), sem acesso a banco.
/// </summary>
[AllowAnonymous]
[PermitirSemAceiteTermos]
public sealed class SiteController : Controller
{
    private const int MaxRelacionadas = 6;

    /// <summary>Páginas públicas fora das de erro (entram no sitemap).</summary>
    public static readonly string[] PaginasFixas = { "/", "/precos", "/erros-nfse", "/ajuda/certificado-digital-a1", "/termos", "/privacidade" };

    [AcceptVerbs("GET", "HEAD", Route = "/erros-nfse")]
    public IActionResult Erros() => View(new ErrosNfseViewModel(CatalogoRejeicoesSefin.Todas));

    [AcceptVerbs("GET", "HEAD", Route = "/erros-nfse/{codigo}")]
    public IActionResult Erro(string codigo)
    {
        var normalizado = (codigo ?? string.Empty).Trim().ToUpperInvariant();

        // Um endereço só por código (e0312 → E0312): evita página duplicada no Google.
        if (normalizado != codigo && CatalogoRejeicoesSefin.Todas.Any(r => r.Codigo == normalizado))
            return RedirectPermanent($"/erros-nfse/{normalizado}");

        var rejeicao = CatalogoRejeicoesSefin.Todas.FirstOrDefault(r => r.Codigo == normalizado);
        if (rejeicao is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return View("ErroNaoEncontrado", normalizado);
        }

        var (campo, documento) = CatalogoRejeicoesSefin.OrigemOficial(rejeicao.Codigo);

        var relacionadas = CatalogoRejeicoesSefin.Todas
            .Where(r => r.Area == rejeicao.Area && r.Codigo != rejeicao.Codigo)
            .OrderBy(r => Math.Abs(Numero(r.Codigo) - Numero(rejeicao.Codigo)))
            .Take(MaxRelacionadas)
            .OrderBy(r => r.Codigo, StringComparer.Ordinal)
            .ToList();

        return View(new ErroNfseViewModel(rejeicao, campo, documento, relacionadas));
    }

    /// <summary>Planos e preços (do catálogo de Plataforma › Planos), comparação com o portal gratuito e dúvidas de cobrança.</summary>
    [AcceptVerbs("GET", "HEAD", Route = "/precos")]
    public IActionResult Precos() => View();

    /// <summary>Como conseguir o certificado digital A1 (.pfx): o passo que mais trava quem começa.</summary>
    [AcceptVerbs("GET", "HEAD", Route = "/ajuda/certificado-digital-a1")]
    public IActionResult AjudaCertificado() => View();

    [AcceptVerbs("GET", "HEAD", Route = "/sitemap.xml")]
    public IActionResult Sitemap()
    {
        var baseUrl = BaseUrl();
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        sb.AppendLine("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""");

        foreach (var caminho in PaginasFixas)
            sb.AppendLine($"  <url><loc>{WebUtility.HtmlEncode(baseUrl + caminho)}</loc></url>");

        foreach (var rejeicao in CatalogoRejeicoesSefin.Todas)
            sb.AppendLine($"  <url><loc>{WebUtility.HtmlEncode($"{baseUrl}/erros-nfse/{rejeicao.Codigo}")}</loc></url>");

        sb.AppendLine("</urlset>");
        return Content(sb.ToString(), "application/xml", Encoding.UTF8);
    }

    // Libera tudo e bloqueia só o privado (lista de "permitidos" esquece o
    // próprio /sitemap.xml e páginas novas). As telas do sistema exigem
    // login: o robô é mandado para /Account/Login, que está bloqueado.
    [AcceptVerbs("GET", "HEAD", Route = "/robots.txt")]
    public IActionResult Robots()
    {
        var texto = $"""
            User-agent: *
            Allow: /Account/Registrar
            Disallow: /Account/
            Disallow: /api/
            Disallow: /hangfire
            Disallow: /Legal/
            Allow: /

            Sitemap: {BaseUrl()}/sitemap.xml
            """;
        return Content(texto.Replace("\r\n", "\n") + "\n", "text/plain", Encoding.UTF8);
    }

    // "E0312" → 312 (os códigos do catálogo são "E" + dígitos).
    private static int Numero(string codigo) => int.TryParse(codigo.AsSpan(1), out var n) ? n : 0;

    // Esquema e host da própria requisição (o Nginx repassa X-Forwarded-Proto;
    // ver UseForwardedHeaders no Program.cs).
    private string BaseUrl() => $"{Request.Scheme}://{Request.Host}";
}

public sealed record ErrosNfseViewModel(IReadOnlyList<RejeicaoSefinTraduzida> Rejeicoes);

public sealed record ErroNfseViewModel(
    RejeicaoSefinTraduzida Rejeicao,
    string? CampoXml,
    string? DocumentoOficial,
    IReadOnlyList<RejeicaoSefinTraduzida> Relacionadas);
