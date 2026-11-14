using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Legal;
using NfseSaaS.Domain.Legal;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>
/// Termos de uso e Política de privacidade (públicos, /termos e
/// /privacidade) e a tela de aceite para quem está logado sem aceite da
/// versão vigente (ExigeAceiteTermosFilter manda pra cá). O aceite em si é
/// gravado por api/legal/aceitar (Controllers/Api/LegalController).
/// </summary>
/// <param name="Desde">Versão dos documentos legais em que esta versão do instrumento entrou (publicação e vigência).</param>
public sealed record InstrumentoLegalViewModel(
    TipoDocumentoLegal Tipo, TextoInstrumento Texto, VersaoDocumentosLegais Desde, IReadOnlyList<string> Versoes, bool EhAtual);

[PermitirSemAceiteTermos]
public sealed class LegalController : Controller
{
    private readonly IAceiteTermosService _aceiteTermos;
    private readonly IAceiteInstrumentosService _instrumentos;
    private readonly ICurrentUser _currentUser;

    public LegalController(IAceiteTermosService aceiteTermos, IAceiteInstrumentosService instrumentos, ICurrentUser currentUser)
    {
        _aceiteTermos = aceiteTermos;
        _instrumentos = instrumentos;
        _currentUser = currentUser;
    }

    /// <summary>Texto mais novo já publicado (DocumentosLegais.Atual): no aviso prévio, já é o da versão nova.</summary>
    [AllowAnonymous]
    [HttpGet("/termos")]
    public IActionResult Termos() => TextoVersao("Termos", DocumentosLegais.Atual(DataBrasilia.Hoje()).Versao);

    [AllowAnonymous]
    [HttpGet("/privacidade")]
    public IActionResult Privacidade() => TextoVersao("Privacidade", DocumentosLegais.Atual(DataBrasilia.Hoje()).Versao);

    /// <summary>Qualquer versão já publicada, como foi publicada (/termos/1.0).</summary>
    [AllowAnonymous]
    [HttpGet("/termos/{versao}")]
    public IActionResult TermosVersao(string versao) => TextoVersao("Termos", versao);

    /// <inheritdoc cref="TermosVersao"/>
    [AllowAnonymous]
    [HttpGet("/privacidade/{versao}")]
    public IActionResult PrivacidadeVersao(string versao) => TextoVersao("Privacidade", versao);

    private IActionResult TextoVersao(string documento, string versao) =>
        DocumentosLegais.Publicada(versao, DataBrasilia.Hoje()) is { } publicada
            ? View($"{documento}_{publicada.SufixoView}")
            : NotFound();

    /// <summary>JUR1: DPA e autorizações (texto atual ou de uma versão), públicos para leitura antes do aceite.</summary>
    [AllowAnonymous]
    [HttpGet("/dpa/{versao?}")]
    public IActionResult Dpa(string? versao) => Instrumento(TipoDocumentoLegal.Dpa, versao);

    /// <inheritdoc cref="Dpa"/>
    [AllowAnonymous]
    [HttpGet("/autorizacao-certificado/{versao?}")]
    public IActionResult AutorizacaoCertificado(string? versao) => Instrumento(TipoDocumentoLegal.AutorizacaoCertificado, versao);

    /// <inheritdoc cref="Dpa"/>
    [AllowAnonymous]
    [HttpGet("/autorizacao-emissao-automatica/{versao?}")]
    public IActionResult AutorizacaoEmissaoAutomatica(string? versao) => Instrumento(TipoDocumentoLegal.AutorizacaoEmissaoAutomatica, versao);

    private IActionResult Instrumento(TipoDocumentoLegal tipo, string? versao)
    {
        var hoje = DataBrasilia.Hoje();
        // Versões do instrumento já publicadas (em ordem), com a versão dos documentos legais que o trouxe.
        var publicadas = DocumentosLegais.Versoes
            .Where(v => v.Publicada(hoje) && DocumentosLegais.VersaoInstrumento(tipo, v) is not null)
            .GroupBy(v => DocumentosLegais.VersaoInstrumento(tipo, v)!)
            .Select(g => (Versao: g.Key, Desde: g.First()))
            .ToList();
        if (publicadas.Count == 0)
            return NotFound();

        var escolhida = versao is null ? publicadas[^1] : publicadas.FirstOrDefault(p => p.Versao == versao);
        if (escolhida.Versao is null || TextosInstrumentos.Obter(tipo, escolhida.Versao) is not { } texto)
            return NotFound();

        return View("Instrumento", new InstrumentoLegalViewModel(
            tipo, texto, escolhida.Desde, publicadas.Select(p => p.Versao).ToList(), escolhida.Versao == publicadas[^1].Versao));
    }

    [Authorize]
    [HttpGet("/Legal/Aceitar")]
    public async Task<IActionResult> Aceitar(string? returnUrl, CancellationToken cancellationToken)
    {
        // Só volta para endereço do próprio site (evita redirecionamento
        // aberto: /Legal/Aceitar?returnUrl=https://site-falso).
        var destino = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";

        if (_currentUser.UserId is not { } usuarioId)
            return LocalRedirect(destino);

        // Nada a aceitar (Termos da versão atual aceitos e, se administrador, DPA e autorizações em dia): volta.
        var termosPendentes = !await _aceiteTermos.AceitouVersaoAtualAsync(usuarioId, cancellationToken);
        var pendencias = await _instrumentos.PendenciasAsync(usuarioId, cancellationToken);
        if (!termosPendentes && !pendencias.AlgumaPendente)
            return LocalRedirect(destino);

        ViewData["TermosPendentes"] = termosPendentes;
        ViewData["Pendencias"] = pendencias;
        ViewData["ReturnUrl"] = destino;
        return View();
    }
}
