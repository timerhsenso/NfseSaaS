using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.DocumentosEmpresa;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// DOC4: Plataforma › Documentos — catálogo geral de tipos de documento,
/// pacotes por ramo e links oficiais por UF/município. Só o operador da
/// plataforma (dono do SaaS) mexe; as contas só escolhem o que gerenciam.
/// </summary>
[ApiController]
[RequerOperadorPlataforma]
[Route("api/plataforma/documentos")]
public sealed class PlataformaDocumentosController : ControllerBase
{
    private readonly IAdministrarCatalogoDocumentosUseCase _catalogo;

    public PlataformaDocumentosController(IAdministrarCatalogoDocumentosUseCase catalogo)
    {
        _catalogo = catalogo;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken)
        => Ok(await _catalogo.ListarAsync(cancellationToken));

    [HttpPost("tipos")]
    public async Task<IActionResult> CriarTipo([FromBody] SalvarTipoDocumentoRequest request, CancellationToken cancellationToken)
        => Ok(new { tipo = await _catalogo.SalvarTipoAsync(null, request, cancellationToken) });

    [HttpPut("tipos/{tipo:int}")]
    public async Task<IActionResult> AlterarTipo(int tipo, [FromBody] SalvarTipoDocumentoRequest request, CancellationToken cancellationToken)
        => Ok(new { tipo = await _catalogo.SalvarTipoAsync(tipo, request, cancellationToken) });

    /// <summary>Links oficiais por lugar e os links que as empresas cadastraram (para oficializar ou corrigir).</summary>
    [HttpGet("tipos/{tipo:int}/links")]
    public async Task<IActionResult> Links(int tipo, CancellationToken cancellationToken)
        => Ok(await _catalogo.LinksAsync(tipo, cancellationToken));

    /// <summary>Cria, troca ou (com link vazio) remove o link oficial de uma UF ou município.</summary>
    [HttpPut("tipos/{tipo:int}/links")]
    public async Task<IActionResult> SalvarLink(int tipo, [FromBody] SalvarLinkLocalDocumentoRequest request, CancellationToken cancellationToken)
    {
        await _catalogo.SalvarLinkLocalAsync(tipo, request, cancellationToken);
        return NoContent();
    }

    [HttpPost("pacotes")]
    public async Task<IActionResult> CriarPacote([FromBody] SalvarPacoteDocumentoRequest request, CancellationToken cancellationToken)
        => Ok(new { id = await _catalogo.SalvarPacoteAsync(null, request, cancellationToken) });

    [HttpPut("pacotes/{id:guid}")]
    public async Task<IActionResult> AlterarPacote(Guid id, [FromBody] SalvarPacoteDocumentoRequest request, CancellationToken cancellationToken)
        => Ok(new { id = await _catalogo.SalvarPacoteAsync(id, request, cancellationToken) });
}
