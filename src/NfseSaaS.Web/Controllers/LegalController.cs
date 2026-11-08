using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>
/// Termos de uso e Política de privacidade (públicos, /termos e
/// /privacidade) e a tela de aceite para quem está logado sem aceite da
/// versão vigente (ExigeAceiteTermosFilter manda pra cá). O aceite em si é
/// gravado por api/legal/aceitar (Controllers/Api/LegalController).
/// </summary>
[PermitirSemAceiteTermos]
public sealed class LegalController : Controller
{
    private readonly IAceiteTermosService _aceiteTermos;
    private readonly ICurrentUser _currentUser;

    public LegalController(IAceiteTermosService aceiteTermos, ICurrentUser currentUser)
    {
        _aceiteTermos = aceiteTermos;
        _currentUser = currentUser;
    }

    [AllowAnonymous]
    [HttpGet("/termos")]
    public IActionResult Termos() => View();

    [AllowAnonymous]
    [HttpGet("/privacidade")]
    public IActionResult Privacidade() => View();

    /// <summary>Texto da versão anterior, como foi aceito (vale até a véspera da versão atual).</summary>
    [AllowAnonymous]
    [HttpGet("/termos/1.0")]
    public IActionResult Termos10() => View("Termos_1_0");

    /// <inheritdoc cref="Termos10"/>
    [AllowAnonymous]
    [HttpGet("/privacidade/1.0")]
    public IActionResult Privacidade10() => View("Privacidade_1_0");

    [Authorize]
    [HttpGet("/Legal/Aceitar")]
    public async Task<IActionResult> Aceitar(string? returnUrl, CancellationToken cancellationToken)
    {
        // Só volta para endereço do próprio site (evita redirecionamento
        // aberto: /Legal/Aceitar?returnUrl=https://site-falso).
        var destino = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";

        if (_currentUser.UserId is { } usuarioId
            && await _aceiteTermos.AceitouVersaoAtualAsync(usuarioId, cancellationToken))
            return LocalRedirect(destino);

        ViewData["ReturnUrl"] = destino;
        return View();
    }
}
