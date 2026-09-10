using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Application.UseCases.ReajustesContrato;

namespace NfseSaaS.Web.Controllers.Api;

[ApiController]
[Authorize]
[Route("api/contratos")]
public sealed class ContratosController : ControllerBase
{
    private readonly ICadastrarContratoUseCase _cadastrarContrato;
    private readonly IListarContratosUseCase _listarContratos;
    private readonly IObterContratoPorIdUseCase _obterContratoPorId;
    private readonly IAtualizarContratoUseCase _atualizarContrato;
    private readonly IDesativarContratoUseCase _desativarContrato;
    private readonly IReativarContratoUseCase _reativarContrato;
    private readonly IExcluirContratoUseCase _excluirContrato;
    private readonly IRegistrarReajusteUseCase _registrarReajuste;
    private readonly IListarReajustesUseCase _listarReajustes;

    public ContratosController(
        ICadastrarContratoUseCase cadastrarContrato,
        IListarContratosUseCase listarContratos,
        IObterContratoPorIdUseCase obterContratoPorId,
        IAtualizarContratoUseCase atualizarContrato,
        IDesativarContratoUseCase desativarContrato,
        IReativarContratoUseCase reativarContrato,
        IExcluirContratoUseCase excluirContrato,
        IRegistrarReajusteUseCase registrarReajuste,
        IListarReajustesUseCase listarReajustes)
    {
        _cadastrarContrato = cadastrarContrato;
        _listarContratos = listarContratos;
        _obterContratoPorId = obterContratoPorId;
        _atualizarContrato = atualizarContrato;
        _desativarContrato = desativarContrato;
        _reativarContrato = reativarContrato;
        _excluirContrato = excluirContrato;
        _registrarReajuste = registrarReajuste;
        _listarReajustes = listarReajustes;
    }

    [Authorize(Roles = Papeis.Administrador)]
    [HttpPost]
    public async Task<IActionResult> Cadastrar([FromBody] CadastrarContratoRequest request, CancellationToken cancellationToken)
    {
        var contratoId = await _cadastrarContrato.ExecutarAsync(request, cancellationToken);
        return Ok(new { contratoId });
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] Guid empresaId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? busca = null,
        [FromQuery] bool incluirInativos = false,
        [FromQuery] Guid? clienteId = null,
        CancellationToken cancellationToken = default)
    {
        var resultado = await _listarContratos.ExecutarAsync(
            new ListarContratosRequest(empresaId, page, pageSize, busca, incluirInativos, clienteId), cancellationToken);
        return Ok(resultado);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> ObterPorId(Guid id, CancellationToken cancellationToken)
    {
        var contrato = await _obterContratoPorId.ExecutarAsync(id, cancellationToken);
        return Ok(contrato);
    }

    [Authorize(Roles = Papeis.Administrador)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] AtualizarContratoRequest request, CancellationToken cancellationToken)
    {
        await _atualizarContrato.ExecutarAsync(id, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Soft delete (Ativo=false) — reversível via /reativar.</summary>
    [Authorize(Roles = Papeis.Administrador)]
    [HttpPost("{id:guid}/desativar")]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken cancellationToken)
    {
        await _desativarContrato.ExecutarAsync(id, cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = Papeis.Administrador)]
    [HttpPost("{id:guid}/reativar")]
    public async Task<IActionResult> Reativar(Guid id, CancellationToken cancellationToken)
    {
        await _reativarContrato.ExecutarAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Exclusão REAL — bloqueada pelo banco se houver vínculo futuro (ver ExcluirContratoUseCase).</summary>
    [Authorize(Roles = Papeis.Administrador)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken cancellationToken)
    {
        await _excluirContrato.ExecutarAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Único caminho pra mudar o valor de um Contrato depois de criado — grava histórico (ver RegistrarReajusteUseCase).</summary>
    [Authorize(Roles = Papeis.Administrador)]
    [HttpPost("{id:guid}/reajustes")]
    public async Task<IActionResult> RegistrarReajuste(Guid id, [FromBody] RegistrarReajusteRequest request, CancellationToken cancellationToken)
    {
        var reajusteId = await _registrarReajuste.ExecutarAsync(id, request, cancellationToken);
        return Ok(new { reajusteId });
    }

    [HttpGet("{id:guid}/reajustes")]
    public async Task<IActionResult> ListarReajustes(Guid id, CancellationToken cancellationToken)
    {
        var reajustes = await _listarReajustes.ExecutarAsync(id, cancellationToken);
        return Ok(reajustes);
    }
}
