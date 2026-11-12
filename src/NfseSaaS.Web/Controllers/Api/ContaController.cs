using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.Conta;

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

    [HttpPost("lembrete-whatsapp/adiar")]
    public async Task<IActionResult> AdiarLembrete(CancellationToken cancellationToken)
    {
        await _dadosConta.AdiarLembreteWhatsAppAsync(cancellationToken);
        return NoContent();
    }
}
