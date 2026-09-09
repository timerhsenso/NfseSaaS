using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.UseCases.Empresas;

namespace NfseSaaS.Web.Controllers.Api;

[ApiController]
[Authorize]
[Route("api/empresas")]
public sealed class EmpresasController : ControllerBase
{
    private readonly ICadastrarEmpresaUseCase _cadastrarEmpresa;
    private readonly IListarEmpresasUseCase _listarEmpresas;
    private readonly IObterEmpresaPorIdUseCase _obterEmpresaPorId;
    private readonly IAtualizarEmpresaUseCase _atualizarEmpresa;
    private readonly IDesativarEmpresaUseCase _desativarEmpresa;
    private readonly IReativarEmpresaUseCase _reativarEmpresa;
    private readonly IExcluirEmpresaUseCase _excluirEmpresa;

    public EmpresasController(
        ICadastrarEmpresaUseCase cadastrarEmpresa,
        IListarEmpresasUseCase listarEmpresas,
        IObterEmpresaPorIdUseCase obterEmpresaPorId,
        IAtualizarEmpresaUseCase atualizarEmpresa,
        IDesativarEmpresaUseCase desativarEmpresa,
        IReativarEmpresaUseCase reativarEmpresa,
        IExcluirEmpresaUseCase excluirEmpresa)
    {
        _cadastrarEmpresa = cadastrarEmpresa;
        _listarEmpresas = listarEmpresas;
        _obterEmpresaPorId = obterEmpresaPorId;
        _atualizarEmpresa = atualizarEmpresa;
        _desativarEmpresa = desativarEmpresa;
        _reativarEmpresa = reativarEmpresa;
        _excluirEmpresa = excluirEmpresa;
    }

    [Authorize(Roles = Papeis.Administrador)]
    [HttpPost]
    public async Task<IActionResult> Cadastrar([FromBody] CadastrarEmpresaRequest request, CancellationToken cancellationToken)
    {
        var empresaId = await _cadastrarEmpresa.ExecutarAsync(request, cancellationToken);
        return Ok(new { empresaId });
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? busca = null,
        [FromQuery] bool incluirInativas = false,
        CancellationToken cancellationToken = default)
    {
        var resultado = await _listarEmpresas.ExecutarAsync(
            new ListarEmpresasRequest(page, pageSize, busca, incluirInativas), cancellationToken);
        return Ok(resultado);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> ObterPorId(Guid id, CancellationToken cancellationToken)
    {
        var empresa = await _obterEmpresaPorId.ExecutarAsync(id, cancellationToken);
        return Ok(empresa);
    }

    [Authorize(Roles = Papeis.Administrador)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] AtualizarEmpresaRequest request, CancellationToken cancellationToken)
    {
        await _atualizarEmpresa.ExecutarAsync(id, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Soft delete (Ativo=false) — reversível via /reativar. Use quando a Empresa já tem histórico.</summary>
    [Authorize(Roles = Papeis.Administrador)]
    [HttpPost("{id:guid}/desativar")]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken cancellationToken)
    {
        await _desativarEmpresa.ExecutarAsync(id, cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = Papeis.Administrador)]
    [HttpPost("{id:guid}/reativar")]
    public async Task<IActionResult> Reativar(Guid id, CancellationToken cancellationToken)
    {
        await _reativarEmpresa.ExecutarAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Exclusão REAL — só funciona se a Empresa não tiver Cliente, Servico nem Nfse vinculados (422 caso contrário).</summary>
    [Authorize(Roles = Papeis.Administrador)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken cancellationToken)
    {
        await _excluirEmpresa.ExecutarAsync(id, cancellationToken);
        return NoContent();
    }
}
