using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Consultas;
using NfseSaaS.Nacional.Resilience;

namespace NfseSaaS.Infrastructure.Consultas;

/// <summary>
/// Preenchimento de endereço nas telas de Empresa e Cliente:
/// - CEP → ViaCEP (https://viacep.com.br/ws/{cep}/json/), que devolve o
///   código IBGE do município junto com rua, bairro e UF;
/// - código IBGE → nome do município, pela API de localidades do IBGE,
///   para mostrar "Salvador — BA" ao lado do código ao editar um cadastro.
///
/// Feito no servidor (não no navegador) porque a CSP só libera
/// connect-src 'self'. Fora da resiliência global (PollyPolicies): o
/// circuit breaker é compartilhado com a SEFIN e um ViaCEP fora do ar não
/// pode bloquear a emissão. Nome de município não muda: fica em cache.
/// </summary>
public sealed class ConsultaEnderecoClient : IConsultarCepUseCase, IConsultarMunicipioUseCase
{
    private const string UrlViaCep = "https://viacep.com.br/ws/{0}/json/";
    private const string UrlIbgeMunicipio = "https://servicodados.ibge.gov.br/api/v1/localidades/municipios/{0}";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ConsultaEnderecoClient> _logger;

    public ConsultaEnderecoClient(IHttpClientFactory httpClientFactory, IMemoryCache cache, ILogger<ConsultaEnderecoClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
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

        _cache.Set(ChaveMunicipio(dados.Ibge), new ConsultaMunicipioResponse(dados.Ibge, dados.Localidade ?? string.Empty, dados.Uf), TimeSpan.FromDays(30));

        return new ConsultaCepResponse(
            cepLimpo,
            Vazio(dados.Logradouro),
            Vazio(dados.Complemento),
            Vazio(dados.Bairro),
            dados.Localidade ?? string.Empty,
            dados.Uf ?? string.Empty,
            dados.Ibge);
    }

    async Task<ConsultaMunicipioResponse> IConsultarMunicipioUseCase.ExecutarAsync(string codigoMunicipio, CancellationToken cancellationToken)
    {
        var codigo = new string((codigoMunicipio ?? string.Empty).Where(char.IsDigit).ToArray());
        if (codigo.Length != 7)
            throw new RegraNegocioException("O código IBGE do município tem 7 dígitos.");

        if (_cache.TryGetValue(ChaveMunicipio(codigo), out ConsultaMunicipioResponse? emCache) && emCache is not null)
            return emCache;

        var json = await BuscarAsync(string.Format(UrlIbgeMunicipio, codigo), "município", cancellationToken);

        // Código inexistente: o IBGE devolve 200 com corpo vazio ou "[]".
        IbgeMunicipio? municipio = null;
        if (!string.IsNullOrWhiteSpace(json) && json.TrimStart().StartsWith('{'))
            municipio = JsonSerializer.Deserialize<IbgeMunicipio>(json);

        if (municipio?.Nome is null)
            throw new RecursoNaoEncontradoException($"Código IBGE {codigo} não encontrado.");

        var uf = municipio.Microrregiao?.Mesorregiao?.Uf?.Sigla
            ?? municipio.RegiaoImediata?.RegiaoIntermediaria?.Uf?.Sigla;

        var resposta = new ConsultaMunicipioResponse(codigo, municipio.Nome, uf);
        _cache.Set(ChaveMunicipio(codigo), resposta, TimeSpan.FromDays(30));
        return resposta;
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

    private static string ChaveMunicipio(string codigo) => "municipio-ibge:" + codigo;

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

    private sealed class IbgeMunicipio
    {
        [JsonPropertyName("nome")] public string? Nome { get; set; }
        [JsonPropertyName("microrregiao")] public IbgeMicro? Microrregiao { get; set; }
        [JsonPropertyName("regiao-imediata")] public IbgeImediata? RegiaoImediata { get; set; }
    }

    private sealed class IbgeMicro { [JsonPropertyName("mesorregiao")] public IbgeMeso? Mesorregiao { get; set; } }
    private sealed class IbgeMeso { [JsonPropertyName("UF")] public IbgeUf? Uf { get; set; } }
    private sealed class IbgeImediata { [JsonPropertyName("regiao-intermediaria")] public IbgeIntermediaria? RegiaoIntermediaria { get; set; } }
    private sealed class IbgeIntermediaria { [JsonPropertyName("UF")] public IbgeUf? Uf { get; set; } }
    private sealed class IbgeUf { [JsonPropertyName("sigla")] public string? Sigla { get; set; } }
}
