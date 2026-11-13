using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NfseSaaS.Application.UseCases.EmailNota;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers.Api;

/// <param name="Token">O "t" do link do campo [Confirmar recebimento].</param>
/// <param name="Email">O e-mail em que a pessoa recebeu a nota.</param>
public sealed record ConfirmarRecebimentoRequest(string? Token, string? Email);

/// <summary>
/// CR1: público (o cliente que recebeu a nota não tem login). Confirma o
/// recebimento depois de conferir que o e-mail está entre os destinatários
/// dos envios da nota (principal ou cópia).
/// </summary>
[ApiController]
[AllowAnonymous]
[PermitirSemAceiteTermos]
[Route("api/recebimento")]
public sealed class ConfirmacaoRecebimentoController : ControllerBase
{
    private readonly IConfirmacaoRecebimentoUseCase _confirmacoes;

    public ConfirmacaoRecebimentoController(IConfirmacaoRecebimentoUseCase confirmacoes) => _confirmacoes = confirmacoes;

    [EnableRateLimiting("AuthSensivel")]
    [HttpPost("confirmar")]
    public async Task<IActionResult> Confirmar([FromBody] ConfirmarRecebimentoRequest request, CancellationToken cancellationToken)
    {
        var resultado = await _confirmacoes.ConfirmarAsync(request.Token, request.Email, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return Ok(new
        {
            jaConfirmado = resultado.JaConfirmado,
            mensagem = resultado.JaConfirmado
                ? $"{resultado.EmailMascarado} já tinha confirmado o recebimento desta nota. Obrigado!"
                : $"Obrigado! Recebimento confirmado por {resultado.EmailMascarado}. {resultado.Empresa} já pode ver a confirmação."
        });
    }
}
