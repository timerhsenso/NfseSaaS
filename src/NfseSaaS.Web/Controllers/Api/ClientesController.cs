using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.Clientes;

namespace NfseSaaS.Web.Controllers.Api;

[ApiController]
[Authorize]
[Route("api/clientes")]
public sealed class ClientesController : ControllerBase
{
    private readonly ICadastrarClienteUseCase _cadastrarCliente;

    public ClientesController(ICadastrarClienteUseCase cadastrarCliente)
    {
        _cadastrarCliente = cadastrarCliente;
    }

    [HttpPost]
    public async Task<IActionResult> Cadastrar([FromBody] CadastrarClienteRequest request, CancellationToken cancellationToken)
    {
        var clienteId = await _cadastrarCliente.ExecutarAsync(request, cancellationToken);
        return Ok(new { clienteId });
    }
}
