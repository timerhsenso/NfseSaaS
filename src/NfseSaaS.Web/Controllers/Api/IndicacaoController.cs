using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.Indicacao;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>"Indique e ganhe": o link da conta e as contas que vieram por ele. Qualquer usuário logado da conta.</summary>
[ApiController]
[Authorize]
[Route("api/indicacao")]
public sealed class IndicacaoController : ControllerBase
{
    private readonly IObterIndicacaoUseCase _obterIndicacao;

    public IndicacaoController(IObterIndicacaoUseCase obterIndicacao)
    {
        _obterIndicacao = obterIndicacao;
    }

    [HttpGet]
    public async Task<IActionResult> Obter(CancellationToken cancellationToken)
    {
        var indicacao = await _obterIndicacao.ExecutarAsync(cancellationToken);
        var link = $"{Request.Scheme}://{Request.Host}/?ref={indicacao.Codigo}";
        return Ok(new { indicacao.Codigo, link, indicacao.Indicadas });
    }
}
