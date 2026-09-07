using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.Servicos;

namespace NfseSaaS.Web.Controllers.Api;

[ApiController]
[Authorize]
[Route("api/servicos")]
public sealed class ServicosController : ControllerBase
{
    private readonly ICadastrarServicoUseCase _cadastrarServico;

    public ServicosController(ICadastrarServicoUseCase cadastrarServico)
    {
        _cadastrarServico = cadastrarServico;
    }

    [HttpPost]
    public async Task<IActionResult> Cadastrar([FromBody] CadastrarServicoRequest request, CancellationToken cancellationToken)
    {
        var servicoId = await _cadastrarServico.ExecutarAsync(request, cancellationToken);
        return Ok(new { servicoId });
    }
}
