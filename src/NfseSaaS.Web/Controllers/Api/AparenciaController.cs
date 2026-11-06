using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.Aparencia;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// Painel "Aparência" do topo. Qualquer usuário logado lê e salva as
/// PRÓPRIAS preferências; a paleta da conta só o administrador (conferido
/// no serviço, não só escondido na tela).
/// </summary>
[ApiController]
[Authorize]
[Route("api/aparencia")]
public sealed class AparenciaController : ControllerBase
{
    private readonly IAparenciaUseCase _aparencia;

    public AparenciaController(IAparenciaUseCase aparencia)
    {
        _aparencia = aparencia;
    }

    [HttpGet]
    public async Task<IActionResult> Obter(CancellationToken cancellationToken)
        => Ok(await _aparencia.ObterAsync(cancellationToken));

    [HttpPut("preferencias")]
    public async Task<IActionResult> SalvarPreferencias([FromBody] PreferenciasAparencia preferencias, CancellationToken cancellationToken)
        => Ok(await _aparencia.SalvarPreferenciasAsync(preferencias, cancellationToken));

    [HttpPut("paleta")]
    public async Task<IActionResult> SalvarPaleta([FromBody] SalvarPaletaRequest request, CancellationToken cancellationToken)
    {
        await _aparencia.SalvarPaletaAsync(request, cancellationToken);
        return NoContent();
    }
}
