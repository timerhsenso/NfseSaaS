using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.Seguranca;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>
/// Tela "Ligue a verificação em duas etapas" (SEG1): para onde o
/// ExigeDuploFatorFilter manda quem passou do prazo. Usa a mesma API do
/// menu do usuário (DuploFatorController). Já ligada ou não exigida: volta
/// para onde ia.
/// </summary>
[Authorize]
[PermitirSemDuploFator]
public sealed class SegurancaController : Controller
{
    private readonly IExigenciaDuploFatorService _exigencia;
    private readonly ICurrentUser _currentUser;

    public SegurancaController(IExigenciaDuploFatorService exigencia, ICurrentUser currentUser)
    {
        _exigencia = exigencia;
        _currentUser = currentUser;
    }

    [HttpGet("/Seguranca/DuasEtapas")]
    public async Task<IActionResult> DuasEtapas(string? returnUrl, CancellationToken cancellationToken)
    {
        // Só volta para endereço do próprio site (sem redirecionamento aberto).
        var destino = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        if (_currentUser.UserId is not { } usuarioId)
            return Challenge();

        var exigencia = await _exigencia.AvaliarAsync(usuarioId, cancellationToken);
        if (!exigencia.Pendente)
            return LocalRedirect(destino);

        ViewData["ReturnUrl"] = destino;
        return View(exigencia);
    }
}
