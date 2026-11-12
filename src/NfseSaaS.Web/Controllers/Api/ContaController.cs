using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.Conta;
using NfseSaaS.Application.UseCases.EmailNota;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// Dados da conta (Configurações da conta) e a faixa do WhatsApp no painel
/// inicial. Qualquer usuário logado lê; só o administrador grava (conferido
/// no serviço).
/// </summary>
[ApiController]
[Authorize]
[Route("api/conta")]
public sealed class ContaController : ControllerBase
{
    private readonly IDadosContaUseCase _dadosConta;

    public ContaController(IDadosContaUseCase dadosConta)
    {
        _dadosConta = dadosConta;
    }

    [HttpGet("dados")]
    public async Task<IActionResult> Obter(CancellationToken cancellationToken)
        => Ok(await _dadosConta.ObterAsync(cancellationToken));

    [HttpPut("contato")]
    public async Task<IActionResult> SalvarContato([FromBody] SalvarContatoContaRequest request, CancellationToken cancellationToken)
        => Ok(await _dadosConta.SalvarContatoAsync(request, cancellationToken));

    /// <summary>E-mail da nota: modelo da conta (sem empresaId) ou o de uma empresa.</summary>
    [HttpGet("email-nota")]
    public async Task<IActionResult> ObterModeloEmailNota(
        [FromQuery] Guid? empresaId, [FromServices] IModeloEmailNotaUseCase modelos, CancellationToken cancellationToken)
        => Ok(await modelos.ObterAsync(empresaId, cancellationToken));

    /// <summary>Só administrador (conferido no serviço).</summary>
    [HttpPut("email-nota")]
    public async Task<IActionResult> SalvarModeloEmailNota(
        [FromBody] SalvarModeloEmailNotaRequest request, [FromServices] IModeloEmailNotaUseCase modelos, CancellationToken cancellationToken)
        => Ok(await modelos.SalvarAsync(request, cancellationToken));

    /// <summary>"Mandar um teste para mim": o texto da tela, com dados de exemplo.</summary>
    [HttpPost("email-nota/teste")]
    public async Task<IActionResult> EnviarTesteModeloEmailNota(
        [FromBody] EnviarTesteModeloEmailNotaRequest request, [FromServices] IModeloEmailNotaUseCase modelos, CancellationToken cancellationToken)
        => Ok(new { email = await modelos.EnviarTesteAsync(request, cancellationToken) });

    [HttpPost("lembrete-whatsapp/adiar")]
    public async Task<IActionResult> AdiarLembrete(CancellationToken cancellationToken)
    {
        await _dadosConta.AdiarLembreteWhatsAppAsync(cancellationToken);
        return NoContent();
    }
}
