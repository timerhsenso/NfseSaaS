using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.Nfse;

namespace NfseSaaS.Web.Controllers.Api;

[ApiController]
[Authorize]
[Route("api/nfse")]
public sealed class NfseController : ControllerBase
{
    private readonly IEmitirNfseUseCase _emitirNfse;

    public NfseController(IEmitirNfseUseCase emitirNfse)
    {
        _emitirNfse = emitirNfse;
    }

    [HttpPost("emitir")]
    public async Task<IActionResult> Emitir([FromBody] EmitirNfseRequest request, CancellationToken cancellationToken)
    {
        var resultado = await _emitirNfse.ExecutarAsync(request, cancellationToken);
        return Ok(resultado);
    }
}
