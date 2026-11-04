using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.Plataforma;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// Operação da plataforma (dono do SaaS) — enxerga TODAS as contas. Fora da
/// matriz de permissões do tenant: só quem está em
/// Plataforma:UsuariosOperadoresIds (RequerOperadorPlataforma).
/// </summary>
[ApiController]
[RequerOperadorPlataforma]
[Route("api/plataforma")]
public sealed class PlataformaController : ControllerBase
{
    private readonly IListarTenantsPlataformaUseCase _listar;
    private readonly IAlterarBloqueioTenantUseCase _alterarBloqueio;
    private readonly ICurrentUser _currentUser;

    public PlataformaController(IListarTenantsPlataformaUseCase listar, IAlterarBloqueioTenantUseCase alterarBloqueio, ICurrentUser currentUser)
    {
        _listar = listar;
        _alterarBloqueio = alterarBloqueio;
        _currentUser = currentUser;
    }

    [HttpGet("tenants")]
    public async Task<IActionResult> ListarTenants(CancellationToken cancellationToken)
        => Ok(await _listar.ExecutarAsync(cancellationToken));

    [HttpPost("tenants/{id:guid}/suspender")]
    public async Task<IActionResult> Suspender(Guid id, [FromBody] SuspenderTenantRequest request, CancellationToken cancellationToken)
    {
        await _alterarBloqueio.ExecutarAsync(id, true, request.Motivo, _currentUser.UserId, cancellationToken);
        return NoContent();
    }

    [HttpPost("tenants/{id:guid}/reativar")]
    public async Task<IActionResult> Reativar(Guid id, CancellationToken cancellationToken)
    {
        await _alterarBloqueio.ExecutarAsync(id, false, null, _currentUser.UserId, cancellationToken);
        return NoContent();
    }
}
