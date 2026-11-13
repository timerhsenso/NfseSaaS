using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.EmailNota;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers;

/// <summary>
/// CR1: página pública do link [Confirmar recebimento]. Só mostra; a
/// confirmação vai por api/recebimento/confirmar (POST), para que robô que
/// abre link (antivírus, pré-visualização do e-mail) não confirme sozinho.
/// </summary>
[AllowAnonymous]
[PermitirSemAceiteTermos]
public sealed class RecebimentoController : Controller
{
    private readonly IConfirmacaoRecebimentoUseCase _confirmacoes;

    public RecebimentoController(IConfirmacaoRecebimentoUseCase confirmacoes) => _confirmacoes = confirmacoes;

    [HttpGet("/recebimento/confirmar")]
    public async Task<IActionResult> Confirmar(string? t, CancellationToken cancellationToken)
    {
        ViewData["Token"] = t;
        var contexto = await _confirmacoes.AbrirAsync(t, cancellationToken);
        return View(contexto);
    }

    /// <summary>Destino do botão na prévia e no "Mandar um teste para mim": mostra a página sem confirmar nada.</summary>
    [HttpGet("/recebimento/exemplo")]
    public IActionResult Exemplo() => View();
}
