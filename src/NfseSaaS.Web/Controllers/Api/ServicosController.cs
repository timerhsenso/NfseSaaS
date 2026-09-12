using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.UseCases.Servicos;

namespace NfseSaaS.Web.Controllers.Api;

[ApiController]
[RequerPermissao(TelaCatalogo.Servicos, AcaoPermissao.Consultar)]
[Route("api/servicos")]
public sealed class ServicosController : ControllerBase
{
    private readonly ICadastrarServicoUseCase _cadastrarServico;
    private readonly IListarServicosUseCase _listarServicos;
    private readonly IObterServicoPorIdUseCase _obterServicoPorId;
    private readonly IAtualizarServicoUseCase _atualizarServico;
    private readonly IDesativarServicoUseCase _desativarServico;
    private readonly IReativarServicoUseCase _reativarServico;
    private readonly IExcluirServicoUseCase _excluirServico;

    public ServicosController(
        ICadastrarServicoUseCase cadastrarServico,
        IListarServicosUseCase listarServicos,
        IObterServicoPorIdUseCase obterServicoPorId,
        IAtualizarServicoUseCase atualizarServico,
        IDesativarServicoUseCase desativarServico,
        IReativarServicoUseCase reativarServico,
        IExcluirServicoUseCase excluirServico)
    {
        _cadastrarServico = cadastrarServico;
        _listarServicos = listarServicos;
        _obterServicoPorId = obterServicoPorId;
        _atualizarServico = atualizarServico;
        _desativarServico = desativarServico;
        _reativarServico = reativarServico;
        _excluirServico = excluirServico;
    }

    [RequerPermissao(TelaCatalogo.Servicos, AcaoPermissao.Incluir)]
    [HttpPost]
    public async Task<IActionResult> Cadastrar([FromBody] CadastrarServicoRequest request, CancellationToken cancellationToken)
    {
        var servicoId = await _cadastrarServico.ExecutarAsync(request, cancellationToken);
        return Ok(new { servicoId });
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
        var resultado = await _listarServicos.ExecutarAsync(
            new ListarServicosRequest(empresaId, page, pageSize, busca, incluirInativos), cancellationToken);
        return Ok(resultado);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> ObterPorId(Guid id, CancellationToken cancellationToken)
    {
        var servico = await _obterServicoPorId.ExecutarAsync(id, cancellationToken);
        return Ok(servico);
    }

    [RequerPermissao(TelaCatalogo.Servicos, AcaoPermissao.Alterar)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] AtualizarServicoRequest request, CancellationToken cancellationToken)
    {
        await _atualizarServico.ExecutarAsync(id, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Soft delete (Ativo=false) — reversível via /reativar.</summary>
    [RequerPermissao(TelaCatalogo.Servicos, AcaoPermissao.Alterar)]
    [HttpPost("{id:guid}/desativar")]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken cancellationToken)
    {
        await _desativarServico.ExecutarAsync(id, cancellationToken);
        return NoContent();
    }

    [RequerPermissao(TelaCatalogo.Servicos, AcaoPermissao.Alterar)]
    [HttpPost("{id:guid}/reativar")]
    public async Task<IActionResult> Reativar(Guid id, CancellationToken cancellationToken)
    {
        await _reativarServico.ExecutarAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Exclusão REAL — sempre permitida (Nfse não referencia ServicoId, ver ExcluirServicoUseCase).</summary>
    [RequerPermissao(TelaCatalogo.Servicos, AcaoPermissao.Excluir)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken cancellationToken)
    {
        await _excluirServico.ExecutarAsync(id, cancellationToken);
        return NoContent();
    }
}
