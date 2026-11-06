using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.Indicacao;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>"Indique e ganhe": o link da conta, as contas que vieram por ele e o convite por e-mail. Qualquer usuário logado da conta.</summary>
[ApiController]
[Authorize]
[Route("api/indicacao")]
public sealed class IndicacaoController : ControllerBase
{
    private readonly IObterIndicacaoUseCase _obterIndicacao;
    private readonly IConvidarPorEmailUseCase _convidar;

    public IndicacaoController(IObterIndicacaoUseCase obterIndicacao, IConvidarPorEmailUseCase convidar)
    {
        _obterIndicacao = obterIndicacao;
        _convidar = convidar;
    }

    [HttpGet]
    public async Task<IActionResult> Obter(CancellationToken cancellationToken)
    {
        var indicacao = await _obterIndicacao.ExecutarAsync(cancellationToken);
        return Ok(new
        {
            indicacao.Codigo,
            link = Link(indicacao.Codigo),
            indicacao.Indicadas,
            indicacao.Convites,
            indicacao.ConvitesRestamHoje,
            limitePorDia = RegrasConviteIndicacao.LimitePorDia
        });
    }

    /// <summary>Convite por e-mail com o link da conta (até 10 por dia; mesmo endereço 1x a cada 30 dias).</summary>
    [HttpPost("convites")]
    public async Task<IActionResult> Convidar([FromBody] ConvidarPorEmailRequest request, CancellationToken cancellationToken)
    {
        var indicacao = await _obterIndicacao.ExecutarAsync(cancellationToken);
        return Ok(await _convidar.ExecutarAsync(request, Link(indicacao.Codigo), cancellationToken));
    }

    private string Link(string codigo) => $"{Request.Scheme}://{Request.Host}/?ref={codigo}";
}
