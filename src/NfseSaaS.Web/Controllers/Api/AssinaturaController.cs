using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>Assinatura da conta: situação, assinar e cancelar. Tela "Assinatura" da matriz de permissões.</summary>
[ApiController]
[RequerPermissao(TelaCatalogo.Assinatura, AcaoPermissao.Consultar)]
[Route("api/assinatura")]
public sealed class AssinaturaController : ControllerBase
{
    private readonly IObterAssinaturaUseCase _obter;
    private readonly IAssinarUseCase _assinar;
    private readonly ICancelarAssinaturaUseCase _cancelar;

    public AssinaturaController(IObterAssinaturaUseCase obter, IAssinarUseCase assinar, ICancelarAssinaturaUseCase cancelar)
    {
        _obter = obter;
        _assinar = assinar;
        _cancelar = cancelar;
    }

    [HttpGet]
    public async Task<IActionResult> Obter(CancellationToken cancellationToken)
        => Ok(await _obter.ExecutarAsync(cancellationToken));

    [HttpPost("assinar")]
    [RequerPermissao(TelaCatalogo.Assinatura, AcaoPermissao.Alterar)]
    public async Task<IActionResult> Assinar([FromBody] AssinarRequest request, CancellationToken cancellationToken)
        => Ok(await _assinar.ExecutarAsync(request, cancellationToken));

    [HttpPost("cancelar")]
    [RequerPermissao(TelaCatalogo.Assinatura, AcaoPermissao.Excluir)]
    public async Task<IActionResult> Cancelar(CancellationToken cancellationToken)
    {
        await _cancelar.ExecutarAsync(cancellationToken);
        return NoContent();
    }
}
