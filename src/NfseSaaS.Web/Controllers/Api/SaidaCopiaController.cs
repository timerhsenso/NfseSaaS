using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers.Api;

/// <param name="Token">O "t" do link do rodapé do e-mail da nota.</param>
/// <param name="Email">O e-mail que quer sair das cópias.</param>
public sealed record PedirSaidaCopiaRequest(string? Token, string? Email);

/// <param name="Codigo">O "c" do link de confirmação.</param>
public sealed record ConfirmarSaidaCopiaRequest(string? Codigo);

/// <summary>
/// RC1: público (quem recebeu a nota como cópia não tem login). Pedir manda
/// uma confirmação para o próprio endereço; só a confirmação (POST) tira o
/// e-mail das cópias. O destinatário principal nunca sai por aqui.
/// </summary>
[ApiController]
[AllowAnonymous]
[PermitirSemAceiteTermos]
[Route("api/copia")]
public sealed class SaidaCopiaController : ControllerBase
{
    private readonly ISaidaCopiaEmailUseCase _saidas;

    public SaidaCopiaController(ISaidaCopiaEmailUseCase saidas) => _saidas = saidas;

    [EnableRateLimiting("Recuperacao")]
    [HttpPost("pedir")]
    public async Task<IActionResult> Pedir([FromBody] PedirSaidaCopiaRequest request, CancellationToken cancellationToken)
    {
        var resultado = await _saidas.PedirAsync(request.Token, request.Email, Ip(), cancellationToken);
        if (resultado == ResultadoPedidoSaidaCopia.Principal)
        {
            var contexto = await _saidas.AbrirAsync(request.Token, cancellationToken);
            var empresa = contexto?.Empresa ?? "o fornecedor";
            return Ok(new
            {
                principal = true,
                mensagem = $"Este é o e-mail principal do cadastro e não sai por aqui. Para mudar, responda o e-mail da nota e fale com {empresa}."
            });
        }

        return Ok(new
        {
            principal = false,
            mensagem = "Se este e-mail recebe as notas como cópia, enviamos para ele um link de confirmação (vale por 24 horas). Confira a caixa de entrada e o spam."
        });
    }

    [EnableRateLimiting("AuthSensivel")]
    [HttpPost("confirmar")]
    public async Task<IActionResult> Confirmar([FromBody] ConfirmarSaidaCopiaRequest request, CancellationToken cancellationToken)
    {
        var confirmacao = await _saidas.ConfirmarAsync(request.Codigo, Ip(), cancellationToken);
        return Ok(new
        {
            mensagem = $"Pronto: {confirmacao.EmailMascarado} não recebe mais as notas de {confirmacao.Empresa} como cópia. Avisamos o fornecedor."
        });
    }

    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
