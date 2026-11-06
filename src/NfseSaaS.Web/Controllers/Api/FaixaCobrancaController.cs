using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Infrastructure.Identity;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// Faixa de cobrança no topo do painel (fatura em aberto, atraso, bloqueio,
/// teste acabando, limite do plano). Para qualquer usuário logado da conta, porque o bloqueio
/// afeta quem emite; o link para pagar só vai para quem pode consultar a
/// tela Assinatura. 204 = nada a mostrar (inclusive com a cobrança desligada).
/// </summary>
[ApiController]
[Authorize]
[Route("api/assinatura/faixa")]
public sealed class FaixaCobrancaController : ControllerBase
{
    private readonly IObterFaixaCobrancaUseCase _obter;

    public FaixaCobrancaController(IObterFaixaCobrancaUseCase obter)
    {
        _obter = obter;
    }

    [HttpGet]
    public async Task<IActionResult> Obter(CancellationToken cancellationToken)
    {
        var podeVerAssinatura = PodeConsultarAssinatura(User);
        var faixa = await _obter.ExecutarAsync(podeVerAssinatura, cancellationToken);
        if (faixa is null)
            return NoContent();

        return Ok(new { faixa.Nivel, faixa.Mensagem, faixa.UrlPagar, faixa.SugerirTroca, podeVerAssinatura });
    }

    /// <summary>Mesma regra do RequerPermissao(Assinatura, Consultar), sem barrar a requisição.</summary>
    private static bool PodeConsultarAssinatura(ClaimsPrincipal usuario) =>
        usuario.FindFirstValue(ApplicationUserClaimsPrincipalFactory.PermissaoClaimTypePrefix + TelaCatalogo.Assinatura)?.Contains('C') == true;
}
