using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.Catalogos;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// Catálogos oficiais globais (não por tenant/Empresa) usados como
/// referência em formulários — hoje só cTribNac. Só leitura: estes
/// catálogos são alimentados por seed (ver CodigoTributacaoNacionalSeeder),
/// nunca por CRUD de usuário. Aberto a qualquer usuário autenticado,
/// mesmo raciocínio do ConsultasController.
/// </summary>
[ApiController]
[Authorize]
[Route("api/catalogos")]
public sealed class CatalogosController : ControllerBase
{
    private readonly IBuscarCodigoTributacaoNacionalUseCase _buscarCodigoTributacaoNacional;
    private readonly IBuscarCodigoNbsUseCase _buscarCodigoNbs;

    public CatalogosController(
        IBuscarCodigoTributacaoNacionalUseCase buscarCodigoTributacaoNacional,
        IBuscarCodigoNbsUseCase buscarCodigoNbs)
    {
        _buscarCodigoTributacaoNacional = buscarCodigoTributacaoNacional;
        _buscarCodigoNbs = buscarCodigoNbs;
    }

    /// <summary>
    /// Formato de resposta já pensado pro select2 (AJAX data source):
    /// { results: [{ id, text }] }. limite tem teto de 50 — é uma busca
    /// de autocomplete, não uma listagem paginada.
    /// </summary>
    [HttpGet("ctribnac")]
    public async Task<IActionResult> BuscarCTribNac([FromQuery] string? busca, [FromQuery] int limite, CancellationToken cancellationToken)
    {
        var limiteReal = limite is > 0 and <= 50 ? limite : 20;
        var resultado = await _buscarCodigoTributacaoNacional.BuscarAsync(busca, limiteReal, cancellationToken);

        return Ok(new
        {
            results = resultado.Select(c => new { id = c.Codigo, text = $"{FormatarCodigo(c.Codigo)} — {c.Descricao}" })
        });
    }

    /// <summary>Mesmo formato de BuscarCTribNac, pro select2 do campo Código NBS.</summary>
    [HttpGet("nbs")]
    public async Task<IActionResult> BuscarNbs([FromQuery] string? busca, [FromQuery] int limite, CancellationToken cancellationToken)
    {
        var limiteReal = limite is > 0 and <= 50 ? limite : 20;
        var resultado = await _buscarCodigoNbs.BuscarAsync(busca, limiteReal, cancellationToken);

        return Ok(new
        {
            results = resultado.Select(c => new { id = c.Codigo, text = $"{FormatarCodigoNbs(c.Codigo)} — {c.Descricao}" })
        });
    }

    /// <summary>"010601" -> "01.06.01", só pra exibição — o valor salvo continua sem pontuação.</summary>
    private static string FormatarCodigo(string codigo) =>
        codigo.Length == 6 ? $"{codigo[..2]}.{codigo[2..4]}.{codigo[4..]}" : codigo;

    /// <summary>"101011100" -> "1.0101.11.00", formato oficial do NBS (item.subitem.posição.subposição).</summary>
    private static string FormatarCodigoNbs(string codigo) =>
        codigo.Length == 9 ? $"{codigo[..1]}.{codigo[1..5]}.{codigo[5..7]}.{codigo[7..]}" : codigo;
}
