using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.UseCases.NotaMensal;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>Emissão em lote (Fase 7) — Contratos Mensais/Ativos de uma Empresa, numa competência.</summary>
[ApiController]
[RequerPermissao(TelaCatalogo.Nfse, AcaoPermissao.Consultar)]
[Route("api/nota-mensal")]
public sealed class NotaMensalController : ControllerBase
{
    private readonly IListarCandidatosNotaMensalUseCase _listarCandidatos;
    private readonly IEmitirNotaMensalLoteUseCase _emitirLote;

    public NotaMensalController(IListarCandidatosNotaMensalUseCase listarCandidatos, IEmitirNotaMensalLoteUseCase emitirLote)
    {
        _listarCandidatos = listarCandidatos;
        _emitirLote = emitirLote;
    }

    [HttpGet("candidatos")]
    public async Task<IActionResult> ListarCandidatos([FromQuery] Guid empresaId, [FromQuery] DateOnly competencia, CancellationToken cancellationToken)
    {
        var candidatos = await _listarCandidatos.ExecutarAsync(empresaId, competencia, cancellationToken);
        return Ok(candidatos);
    }

    [RequerPermissao(TelaCatalogo.Nfse, AcaoPermissao.Incluir)]
    [HttpPost("emitir")]
    public async Task<IActionResult> EmitirLote([FromBody] EmitirNotaMensalLoteRequest request, CancellationToken cancellationToken)
    {
        var resultados = await _emitirLote.ExecutarAsync(request, cancellationToken);
        return Ok(resultados);
    }
}
