using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.Indicacao;
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
    private readonly IConsultarProtecaoChavesUseCase _consultarProtecaoChaves;
    private readonly IReprotegerChavesUseCase _reprotegerChaves;
    private readonly IListarPremiosIndicacaoPlataformaUseCase _listarPremios;
    private readonly IReabrirPremioIndicacaoUseCase _reabrirPremio;
    private readonly ICurrentUser _currentUser;

    public PlataformaController(
        IListarTenantsPlataformaUseCase listar,
        IAlterarBloqueioTenantUseCase alterarBloqueio,
        IConsultarProtecaoChavesUseCase consultarProtecaoChaves,
        IReprotegerChavesUseCase reprotegerChaves,
        IListarPremiosIndicacaoPlataformaUseCase listarPremios,
        IReabrirPremioIndicacaoUseCase reabrirPremio,
        ICurrentUser currentUser)
    {
        _listar = listar;
        _alterarBloqueio = alterarBloqueio;
        _consultarProtecaoChaves = consultarProtecaoChaves;
        _reprotegerChaves = reprotegerChaves;
        _listarPremios = listarPremios;
        _reabrirPremio = reabrirPremio;
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

    /// <summary>Prêmios do "Indique e ganhe" (os 300 mais recentes).</summary>
    [HttpGet("premios-indicacao")]
    public async Task<IActionResult> PremiosIndicacao(CancellationToken cancellationToken)
        => Ok(await _listarPremios.ExecutarAsync(cancellationToken));

    /// <summary>Devolve um prêmio com erro para a fila (conferir antes no Asaas).</summary>
    [HttpPost("premios-indicacao/{id:guid}/tentar-novamente")]
    public async Task<IActionResult> TentarPremioNovamente(Guid id, CancellationToken cancellationToken)
    {
        await _reabrirPremio.ExecutarAsync(id, _currentUser.UserId, cancellationToken);
        return NoContent();
    }

    /// <summary>Situação das chaves do Data Protection (criptografadas ou não).</summary>
    [HttpGet("protecao-chaves")]
    public async Task<IActionResult> ProtecaoChaves(CancellationToken cancellationToken)
        => Ok(await _consultarProtecaoChaves.ExecutarAsync(cancellationToken));

    /// <summary>Regrava os certificados com uma chave nova criptografada e apaga as chaves antigas sem criptografia.</summary>
    [HttpPost("protecao-chaves/reproteger")]
    public async Task<IActionResult> ReprotegerChaves(CancellationToken cancellationToken)
        => Ok(await _reprotegerChaves.ExecutarAsync(_currentUser.UserId, cancellationToken));
}
