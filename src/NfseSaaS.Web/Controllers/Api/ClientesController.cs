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
    private readonly IListarClientesUseCase _listarClientes;
    private readonly IObterClientePorIdUseCase _obterClientePorId;
    private readonly IAtualizarClienteUseCase _atualizarCliente;
    private readonly IDesativarClienteUseCase _desativarCliente;
    private readonly IReativarClienteUseCase _reativarCliente;
    private readonly IExcluirClienteUseCase _excluirCliente;

    public ClientesController(
        ICadastrarClienteUseCase cadastrarCliente,
        IListarClientesUseCase listarClientes,
        IObterClientePorIdUseCase obterClientePorId,
        IAtualizarClienteUseCase atualizarCliente,
        IDesativarClienteUseCase desativarCliente,
        IReativarClienteUseCase reativarCliente,
        IExcluirClienteUseCase excluirCliente)
    {
        _cadastrarCliente = cadastrarCliente;
        _listarClientes = listarClientes;
        _obterClientePorId = obterClientePorId;
        _atualizarCliente = atualizarCliente;
        _desativarCliente = desativarCliente;
        _reativarCliente = reativarCliente;
        _excluirCliente = excluirCliente;
    }

    [HttpPost]
    public async Task<IActionResult> Cadastrar([FromBody] CadastrarClienteRequest request, CancellationToken cancellationToken)
    {
        var clienteId = await _cadastrarCliente.ExecutarAsync(request, cancellationToken);
        return Ok(new { clienteId });
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] Guid empresaId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? busca = null,
        [FromQuery] bool incluirInativos = false,
        CancellationToken cancellationToken = default)
    {
        var resultado = await _listarClientes.ExecutarAsync(
            new ListarClientesRequest(empresaId, page, pageSize, busca, incluirInativos), cancellationToken);
        return Ok(resultado);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> ObterPorId(Guid id, CancellationToken cancellationToken)
    {
        var cliente = await _obterClientePorId.ExecutarAsync(id, cancellationToken);
        return Ok(cliente);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] AtualizarClienteRequest request, CancellationToken cancellationToken)
    {
        await _atualizarCliente.ExecutarAsync(id, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Soft delete (Ativo=false) — reversível via /reativar. Use quando o Cliente já tem histórico (Nfse).</summary>
    [HttpPost("{id:guid}/desativar")]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken cancellationToken)
    {
        await _desativarCliente.ExecutarAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reativar")]
    public async Task<IActionResult> Reativar(Guid id, CancellationToken cancellationToken)
    {
        await _reativarCliente.ExecutarAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Exclusão REAL — só funciona se o Cliente não tiver Nfse vinculada (422 caso contrário).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken cancellationToken)
    {
        await _excluirCliente.ExecutarAsync(id, cancellationToken);
        return NoContent();
    }
}
