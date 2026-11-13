using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.EmailNota;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// Cadastro "Modelos de e-mail" (texto do e-mail que leva a nota ao
/// cliente). Qualquer usuário logado lê (os campos "Modelo do e-mail" de
/// Empresa e Cliente usam as opções); só o administrador grava (conferido
/// no serviço).
/// </summary>
[ApiController]
[Authorize]
[Route("api/modelos-email-nota")]
public sealed class ModelosEmailNotaController : ControllerBase
{
    private readonly IModeloEmailNotaUseCase _modelos;

    public ModelosEmailNotaController(IModeloEmailNotaUseCase modelos)
    {
        _modelos = modelos;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken)
        => Ok(await _modelos.ListarAsync(cancellationToken));

    /// <summary>Só id, nome e padrão — para os campos de escolha.</summary>
    [HttpGet("opcoes")]
    public async Task<IActionResult> Opcoes(CancellationToken cancellationToken)
        => Ok(await _modelos.OpcoesAsync(cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] SalvarModeloEmailNotaRequest request, CancellationToken cancellationToken)
        => Ok(new { id = await _modelos.CriarAsync(request, cancellationToken) });

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] SalvarModeloEmailNotaRequest request, CancellationToken cancellationToken)
    {
        await _modelos.AtualizarAsync(id, request, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/duplicar")]
    public async Task<IActionResult> Duplicar(Guid id, CancellationToken cancellationToken)
        => Ok(new { id = await _modelos.DuplicarAsync(id, cancellationToken) });

    [HttpPost("{id:guid}/padrao")]
    public async Task<IActionResult> DefinirPadrao(Guid id, CancellationToken cancellationToken)
    {
        await _modelos.DefinirPadraoAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Quem usava o modelo excluído volta para o padrão da conta.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken cancellationToken)
    {
        await _modelos.ExcluirAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>"Mandar um teste para mim": o texto da tela, com dados de exemplo e sem anexos.</summary>
    [HttpPost("teste")]
    public async Task<IActionResult> EnviarTeste([FromBody] EnviarTesteModeloEmailNotaRequest request, CancellationToken cancellationToken)
        => Ok(new { email = await _modelos.EnviarTesteAsync(request, cancellationToken) });
}
