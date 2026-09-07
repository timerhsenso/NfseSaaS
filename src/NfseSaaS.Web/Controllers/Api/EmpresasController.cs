using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.Empresas;

namespace NfseSaaS.Web.Controllers.Api;

[ApiController]
[Authorize]
[Route("api/empresas")]
public sealed class EmpresasController : ControllerBase
{
    private readonly ICadastrarEmpresaUseCase _cadastrarEmpresa;

    public EmpresasController(ICadastrarEmpresaUseCase cadastrarEmpresa)
    {
        _cadastrarEmpresa = cadastrarEmpresa;
    }

    [HttpPost]
    public async Task<IActionResult> Cadastrar([FromBody] CadastrarEmpresaRequest request, CancellationToken cancellationToken)
    {
        var empresaId = await _cadastrarEmpresa.ExecutarAsync(request, cancellationToken);
        return Ok(new { empresaId });
    }
}
