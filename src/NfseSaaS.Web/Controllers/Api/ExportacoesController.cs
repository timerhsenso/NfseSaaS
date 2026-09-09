using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.Exportacoes;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// Só grava METADADO de auditoria (quem exportou/imprimiu qual tela,
/// quando) — nunca conteúdo de grid. Aberto a qualquer usuário
/// autenticado do tenant (não é uma ação sensível de escrita de negócio,
/// é o próprio registro de rastreabilidade que a exportação exige).
/// </summary>
[ApiController]
[Authorize]
[Route("api/exportacoes")]
public sealed class ExportacoesController : ControllerBase
{
    private readonly IRegistrarExportacaoUseCase _registrarExportacao;

    public ExportacoesController(IRegistrarExportacaoUseCase registrarExportacao)
    {
        _registrarExportacao = registrarExportacao;
    }

    [HttpPost]
    public async Task<IActionResult> Registrar([FromBody] RegistrarExportacaoRequest request, CancellationToken cancellationToken)
    {
        await _registrarExportacao.ExecutarAsync(request.Tela, request.Formato, cancellationToken);
        return NoContent();
    }
}
