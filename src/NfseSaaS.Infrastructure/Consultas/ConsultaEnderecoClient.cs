using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Consultas;
using NfseSaaS.Nacional.Resilience;

namespace NfseSaaS.Infrastructure.Consultas;

/// <summary>
/// Preenchimento de endereço nas telas de Empresa e Cliente:
/// - CEP → ViaCEP (https://viacep.com.br/ws/{cep}/json/), que devolve o
///   código IBGE do município junto com rua, bairro e UF;
/// (o nome do município pelo código vem da tabela local — MunicipiosService, C9).
///
/// Feito no servidor (não no navegador) porque a CSP só libera
/// connect-src 'self'. Fora da resiliência global (PollyPolicies): o
/// circuit breaker é compartilhado com a SEFIN e um ViaCEP fora do ar não
/// pode bloquear a emissão.
/// </summary>
public sealed class ConsultaEnderecoClient : IConsultarCepUseCase
{
    private const string UrlViaCep = "https://viacep.com.br/ws/{0}/json/";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ConsultaEnderecoClient> _logger;

    public ConsultaEnderecoClient(IHttpClientFactory httpClientFactory, ILogger<ConsultaEnderecoClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ConsultaCepResponse> ExecutarAsync(string cep, CancellationToken cancellationToken)
    {
        var cepLimpo = new string((cep ?? string.Empty).Where(char.IsDigit).ToArray());
        if (cepLimpo.Length != 8)
            throw new RegraNegocioException("CEP deve ter 8 dígitos.");

        var json = await BuscarAsync(string.Format(UrlViaCep, cepLimpo), "CEP", cancellationToken);
        var dados = json is null ? null : JsonSerializer.Deserialize<ViaCepResposta>(json);

        // ViaCEP responde 200 com {"erro": true} (ou "true") para CEP inexistente.
        if (dados is null || dados.Erro is not null || string.IsNullOrWhiteSpace(dados.Ibge))
            throw new RecursoNaoEncontradoException($"CEP {cepLimpo} não encontrado. Confira o número ou preencha o endereço à mão.");

        return new ConsultaCepResponse(
            cepLimpo,
            Vazio(dados.Logradouro),
            Vazio(dados.Complemento),
            Vazio(dados.Bairro),
            dados.Localidade ?? string.Empty,
            dados.Uf ?? string.Empty,
            dados.Ibge);
    }

    private async Task<string?> BuscarAsync(string url, string oque, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("ConsultaEndereco");
        client.Timeout = TimeSpan.FromSeconds(8);

        using var requisicao = new HttpRequestMessage(HttpMethod.Get, url);
        requisicao.Options.Set(PollyPolicies.IgnorarResilienciaGlobal, true);

        try
        {
            using var resposta = await client.SendAsync(requisicao, cancellationToken);
            if (resposta.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
                return null;

            if (!resposta.IsSuccessStatusCode)
            {
                _logger.LogWarning("Consulta de {Oque} respondeu HTTP {Status}.", oque, (int)resposta.StatusCode);
                throw new RegraNegocioException($"Não foi possível consultar o {oque} agora. Preencha à mão.");
            }

            return await resposta.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Falha de comunicação ao consultar {Oque}.", oque);
            throw new RegraNegocioException($"Não foi possível consultar o {oque} agora. Preencha à mão.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RegraNegocioException($"A consulta de {oque} demorou demais. Preencha à mão.");
        }
    }

    private static string? Vazio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private sealed class ViaCepResposta
    {
        [JsonPropertyName("logradouro")] public string? Logradouro { get; set; }
        [JsonPropertyName("complemento")] public string? Complemento { get; set; }
        [JsonPropertyName("bairro")] public string? Bairro { get; set; }
        [JsonPropertyName("localidade")] public string? Localidade { get; set; }
        [JsonPropertyName("uf")] public string? Uf { get; set; }
        [JsonPropertyName("ibge")] public string? Ibge { get; set; }
        // Vem como true ou "true" — JsonElement aceita os dois.
        [JsonPropertyName("erro")] public JsonElement? Erro { get; set; }
    }
}
