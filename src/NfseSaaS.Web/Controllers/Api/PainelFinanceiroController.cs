using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.UseCases.PainelFinanceiro;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// DB1: painel financeiro do início. Mostra só somas do que o usuário já vê
/// nas telas: faturamento exige consultar Notas; receita recorrente, notas a
/// emitir e reajustes exigem também consultar Contratos (sem isso, a parte
/// de contratos vem nula e a tela não mostra).
/// </summary>
[ApiController]
[RequerPermissao(TelaCatalogo.Nfse, AcaoPermissao.Consultar)]
[Route("api/painel-financeiro")]
public sealed class PainelFinanceiroController : ControllerBase
{
    private readonly IObterPainelFinanceiroUseCase _obter;

    public PainelFinanceiroController(IObterPainelFinanceiroUseCase obter)
    {
        _obter = obter;
    }

    /// <param name="empresaAtualId">Empresa escolhida no topo.</param>
    /// <param name="escopo">"todas" ou "empresa"; vazio = padrão do perfil da conta.</param>
    [HttpGet]
    public async Task<IActionResult> Obter([FromQuery] Guid? empresaAtualId, [FromQuery] string? escopo, CancellationToken cancellationToken)
    {
        EscopoPainelFinanceiro? escolhido = escopo?.Trim().ToLowerInvariant() switch
        {
            "todas" => EscopoPainelFinanceiro.Todas,
            "empresa" => EscopoPainelFinanceiro.Empresa,
            _ => null
        };
        return Ok(await _obter.ExecutarAsync(
            new PainelFinanceiroRequest(empresaAtualId, escolhido, PodeConsultarContratos(User)), cancellationToken));
    }

    /// <summary>Mesma regra do RequerPermissao(Contratos, Consultar), sem barrar a requisição.</summary>
    private static bool PodeConsultarContratos(ClaimsPrincipal usuario) =>
        usuario.FindFirstValue(ApplicationUserClaimsPrincipalFactory.PermissaoClaimTypePrefix + TelaCatalogo.Contratos)?.Contains('C') == true;
}
