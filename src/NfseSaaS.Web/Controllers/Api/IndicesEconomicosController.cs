using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.UseCases.IndicesEconomicos;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// Índices econômicos — tabela GLOBAL (não por tenant). Consulta aberta a
/// qualquer usuário autenticado; escrita só para o operador da plataforma,
/// porque um valor errado aqui mudaria o reajuste de todos os tenants.
/// </summary>
[ApiController]
[Authorize]
[Route("api/indices-economicos")]
public sealed class IndicesEconomicosController : ControllerBase
{
    private readonly IListarIndicesDisponiveisUseCase _listarDisponiveis;
    private readonly IListarIndicesEconomicosUseCase _listar;
    private readonly ICalcularIndiceAcumuladoUseCase _calcularAcumulado;
    private readonly ISalvarIndiceManualUseCase _salvarManual;
    private readonly IExcluirIndiceEconomicoUseCase _excluir;
    private readonly IImportarIndicesEconomicosUseCase _importar;

    public IndicesEconomicosController(
        IListarIndicesDisponiveisUseCase listarDisponiveis,
        IListarIndicesEconomicosUseCase listar,
        ICalcularIndiceAcumuladoUseCase calcularAcumulado,
        ISalvarIndiceManualUseCase salvarManual,
        IExcluirIndiceEconomicoUseCase excluir,
        IImportarIndicesEconomicosUseCase importar)
    {
        _listarDisponiveis = listarDisponiveis;
        _listar = listar;
        _calcularAcumulado = calcularAcumulado;
        _salvarManual = salvarManual;
        _excluir = excluir;
        _importar = importar;
    }

    [HttpGet("disponiveis")]
    public async Task<IActionResult> ListarDisponiveis(CancellationToken cancellationToken)
        => Ok(await _listarDisponiveis.ExecutarAsync(cancellationToken));

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] IndiceReajusteContrato indice, CancellationToken cancellationToken)
        => Ok(await _listar.ExecutarAsync(indice, cancellationToken));

    /// <summary>Acumulado de 12 meses para uma data de aniversário (padrão: hoje, horário de Brasília).</summary>
    [HttpGet("acumulado")]
    public async Task<IActionResult> Acumulado(
        [FromQuery] IndiceReajusteContrato indice,
        [FromQuery] DateOnly? dataReferencia,
        CancellationToken cancellationToken)
        => Ok(await _calcularAcumulado.ExecutarAsync(indice, dataReferencia ?? DataBrasilia.Hoje(), cancellationToken));

    [RequerOperadorPlataforma]
    [HttpPut]
    public async Task<IActionResult> SalvarManual([FromBody] SalvarIndiceManualRequest request, CancellationToken cancellationToken)
    {
        await _salvarManual.ExecutarAsync(request, cancellationToken);
        return NoContent();
    }

    [RequerOperadorPlataforma]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken cancellationToken)
    {
        await _excluir.ExecutarAsync(id, cancellationToken);
        return NoContent();
    }

    [RequerOperadorPlataforma]
    [HttpPost("importar")]
    public async Task<IActionResult> Importar(CancellationToken cancellationToken)
        => Ok(await _importar.ExecutarAsync(cancellationToken));
}
