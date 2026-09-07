using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Web.Controllers.Api;

[ApiController]
[Authorize]
[Route("api/nfse")]
public sealed class NfseController : ControllerBase
{
    private readonly IEmitirNfseUseCase _emitirNfse;
    private readonly IListarNfseUseCase _listarNfse;
    private readonly IObterNfsePorIdUseCase _obterNfsePorId;
    private readonly ICancelarNfseUseCase _cancelarNfse;

    public NfseController(
        IEmitirNfseUseCase emitirNfse,
        IListarNfseUseCase listarNfse,
        IObterNfsePorIdUseCase obterNfsePorId,
        ICancelarNfseUseCase cancelarNfse)
    {
        _emitirNfse = emitirNfse;
        _listarNfse = listarNfse;
        _obterNfsePorId = obterNfsePorId;
        _cancelarNfse = cancelarNfse;
    }

    [HttpPost("emitir")]
    public async Task<IActionResult> Emitir([FromBody] EmitirNfseRequest request, CancellationToken cancellationToken)
    {
        var resultado = await _emitirNfse.ExecutarAsync(request, cancellationToken);
        return Ok(resultado);
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] Guid? empresaId = null,
        [FromQuery] NfseStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var resultado = await _listarNfse.ExecutarAsync(
            new ListarNfseRequest(empresaId, status, page, pageSize), cancellationToken);
        return Ok(resultado);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> ObterPorId(Guid id, CancellationToken cancellationToken)
    {
        var nfse = await _obterNfsePorId.ExecutarAsync(id, cancellationToken);
        return Ok(nfse);
    }

    [HttpPost("{id:guid}/cancelar")]
    public async Task<IActionResult> Cancelar(Guid id, [FromBody] CancelarNfseRequest request, CancellationToken cancellationToken)
    {
        var resultado = await _cancelarNfse.ExecutarAsync(id, request, cancellationToken);
        return Ok(resultado);
    }
}
