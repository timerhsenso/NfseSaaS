using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.Seguranca;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// Configurações da conta › Segurança (SEG1): exigir a verificação em duas
/// etapas de todos os usuários e ver quem ainda não ligou. Só o
/// administrador (conferido no serviço); alterar pede a senha recente.
/// </summary>
[ApiController]
[Authorize]
[Route("api/conta/seguranca")]
public sealed class SegurancaContaController : ControllerBase
{
    private readonly ISegurancaContaUseCase _seguranca;

    public SegurancaContaController(ISegurancaContaUseCase seguranca)
    {
        _seguranca = seguranca;
    }

    [HttpGet]
    public async Task<IActionResult> Obter(CancellationToken cancellationToken)
        => Ok(await _seguranca.ObterAsync(cancellationToken));

    [HttpPut]
    [RequerConfirmacaoSenha]
    public async Task<IActionResult> Alterar([FromBody] AlterarSegurancaContaRequest request, CancellationToken cancellationToken)
        => Ok(await _seguranca.AlterarAsync(request, cancellationToken));
}
