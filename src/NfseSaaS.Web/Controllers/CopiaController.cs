using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>
/// RC1: páginas públicas do link do rodapé do e-mail da nota. Só mostram;
/// pedir e confirmar vão por api/copia (POST), para que robô que abre link
/// (antivírus, pré-visualização do e-mail) não confirme nada sozinho.
/// </summary>
[AllowAnonymous]
[PermitirSemAceiteTermos]
public sealed class CopiaController : Controller
{
    private readonly ISaidaCopiaEmailUseCase _saidas;

    public CopiaController(ISaidaCopiaEmailUseCase saidas) => _saidas = saidas;

    [HttpGet("/copia/sair")]
    public async Task<IActionResult> Sair(string? t, CancellationToken cancellationToken)
    {
        ViewData["Token"] = t;
        var contexto = await _saidas.AbrirAsync(t, cancellationToken);
        return View(contexto);
    }

    [HttpGet("/copia/confirmar")]
    public async Task<IActionResult> Confirmar(string? c, CancellationToken cancellationToken)
    {
        ViewData["Codigo"] = c;
        var confirmacao = await _saidas.ObterConfirmacaoAsync(c, cancellationToken);
        return View(confirmacao);
    }
}
