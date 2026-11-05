using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Infrastructure.Cobranca;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// Webhook do Asaas (POST /api/webhooks/asaas). Sem login e sem anti-CSRF:
/// a autenticação é o token configurado no painel do Asaas, que chega no
/// header "asaas-access-token" e é comparado em tempo constante com
/// Asaas:WebhookToken. Sem token configurado, recusa tudo.
/// </summary>
[ApiController]
[AllowAnonymous]
[PermitirSemAceiteTermos]
[Route("api/webhooks")]
public sealed class WebhooksController : ControllerBase
{
    private const int TamanhoMaximoCorpo = 256 * 1024;

    private readonly IProcessarWebhookAsaasUseCase _processar;
    private readonly AsaasOptions _asaas;

    public WebhooksController(IProcessarWebhookAsaasUseCase processar, IOptions<AsaasOptions> asaas)
    {
        _processar = processar;
        _asaas = asaas.Value;
    }

    [HttpPost("asaas")]
    [SemAntiforgery]
    [RequestSizeLimit(TamanhoMaximoCorpo)]
    public async Task<IActionResult> Asaas(CancellationToken cancellationToken)
    {
        if (!TokenValido(Request.Headers["asaas-access-token"].ToString()))
            return Unauthorized();

        using var leitor = new StreamReader(Request.Body, Encoding.UTF8);
        var payload = await leitor.ReadToEndAsync(cancellationToken);

        var resultado = await _processar.ExecutarAsync(payload, cancellationToken);
        return Ok(new { resultado });
    }

    private bool TokenValido(string recebido)
    {
        if (string.IsNullOrWhiteSpace(_asaas.WebhookToken) || string.IsNullOrEmpty(recebido))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(recebido), Encoding.UTF8.GetBytes(_asaas.WebhookToken));
    }
}
