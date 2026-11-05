using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;

namespace NfseSaaS.Infrastructure.Cobranca;

/// <summary>
/// Cliente da API v3 do Asaas (HttpClient tipado). Autenticação pelo header
/// "access_token"; o User-Agent é obrigatório. POST nunca é repetido
/// automaticamente (a política global de retry só repete GET — ver
/// PollyPolicies), para não criar cliente ou assinatura em dobro.
/// </summary>
public sealed class AsaasClient : IGatewayCobranca
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly AsaasOptions _options;
    private readonly ILogger<AsaasClient> _logger;

    public AsaasClient(HttpClient http, IOptions<AsaasOptions> options, ILogger<AsaasClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool Configurado => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<string> CriarClienteAsync(string nome, string cpfCnpj, string? email, string referenciaExterna, CancellationToken cancellationToken)
    {
        var resposta = await EnviarAsync<IdResposta>(HttpMethod.Post, "customers", new
        {
            name = nome,
            cpfCnpj,
            email,
            externalReference = referenciaExterna,
            // As faturas e lembretes saem pelo próprio Asaas (e-mail/SMS do cliente).
            notificationDisabled = false
        }, cancellationToken);
        return resposta.Id;
    }

    public async Task<string> CriarAssinaturaMensalAsync(string clienteId, decimal valor, DateOnly primeiroVencimento, string descricao, string referenciaExterna, CancellationToken cancellationToken)
    {
        var resposta = await EnviarAsync<IdResposta>(HttpMethod.Post, "subscriptions", new
        {
            customer = clienteId,
            // UNDEFINED: o cliente escolhe Pix, boleto ou cartão na página da fatura.
            billingType = "UNDEFINED",
            value = valor,
            nextDueDate = primeiroVencimento.ToString("yyyy-MM-dd"),
            cycle = "MONTHLY",
            description = descricao,
            externalReference = referenciaExterna
        }, cancellationToken);
        return resposta.Id;
    }

    public async Task<string?> ObterUrlFaturaPendenteAsync(string assinaturaId, CancellationToken cancellationToken)
    {
        var lista = await EnviarAsync<ListaCobrancas>(HttpMethod.Get, $"subscriptions/{Uri.EscapeDataString(assinaturaId)}/payments", null, cancellationToken);
        return lista.Data?
            .Where(c => c.Status is "PENDING" or "OVERDUE")
            .OrderBy(c => c.DueDate)
            .Select(c => c.InvoiceUrl)
            .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
    }

    public async Task CancelarAssinaturaAsync(string assinaturaId, CancellationToken cancellationToken)
    {
        await EnviarAsync<JsonElement>(HttpMethod.Delete, $"subscriptions/{Uri.EscapeDataString(assinaturaId)}", null, cancellationToken);
    }

    private async Task<T> EnviarAsync<T>(HttpMethod metodo, string caminho, object? corpo, CancellationToken cancellationToken)
    {
        if (!Configurado)
            throw new RegraNegocioException("A cobrança ainda não está configurada (chave do Asaas ausente). Fale com o suporte.");

        using var requisicao = new HttpRequestMessage(metodo, $"{_options.BaseUrl.TrimEnd('/')}/{caminho}");
        requisicao.Headers.TryAddWithoutValidation("access_token", _options.ApiKey);
        requisicao.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        if (corpo is not null)
            requisicao.Content = JsonContent.Create(corpo, options: Json);

        using var resposta = await _http.SendAsync(requisicao, cancellationToken);
        var texto = await resposta.Content.ReadAsStringAsync(cancellationToken);

        if (!resposta.IsSuccessStatusCode)
        {
            // Nunca loga a chave; o corpo de erro do Asaas não tem dado sensível além do que enviamos.
            _logger.LogError("Asaas {Metodo} {Caminho} respondeu {Status}: {Corpo}", metodo, caminho, (int)resposta.StatusCode, texto);
            throw new RegraNegocioException(MensagemErro(texto) ?? "O sistema de pagamentos recusou a operação. Tente de novo em alguns minutos ou fale com o suporte.");
        }

        return JsonSerializer.Deserialize<T>(texto, Json)
            ?? throw new InvalidOperationException($"Resposta vazia do Asaas em {metodo} {caminho}.");
    }

    /// <summary>Erros do Asaas vêm como {"errors":[{"code":"...","description":"..."}]}.</summary>
    private static string? MensagemErro(string corpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(corpo);
            if (doc.RootElement.TryGetProperty("errors", out var erros) && erros.ValueKind == JsonValueKind.Array)
            {
                var descricoes = erros.EnumerateArray()
                    .Select(e => e.TryGetProperty("description", out var d) ? d.GetString() : null)
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .ToList();
                if (descricoes.Count > 0)
                    return "Pagamento: " + string.Join(" ", descricoes);
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    private sealed record IdResposta(string Id);

    private sealed record CobrancaAsaas(string? Id, string? Status, string? DueDate, string? InvoiceUrl);

    private sealed record ListaCobrancas(List<CobrancaAsaas>? Data);
}
