using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Consultas;

namespace NfseSaaS.Infrastructure.Consultas;

/// <summary>
/// Schema real confirmado contra um exemplo de resposta real da
/// BrasilAPI (não é suposição) — inclui só os campos que este cliente
/// de fato usa. codigo_municipio_ibge é o código de 7 dígitos do IBGE
/// (o mesmo formato que Empresa.CodigoMunicipio já espera); existe
/// também um "codigo_municipio" sem sufixo, que é outro código (da
/// própria Receita) — NUNCA usar esse por engano.
/// </summary>
internal sealed record BrasilApiCnpjResponse(
    [property: JsonPropertyName("cnpj")] string? Cnpj,
    [property: JsonPropertyName("razao_social")] string? RazaoSocial,
    [property: JsonPropertyName("nome_fantasia")] string? NomeFantasia,
    [property: JsonPropertyName("logradouro")] string? Logradouro,
    [property: JsonPropertyName("numero")] string? Numero,
    [property: JsonPropertyName("complemento")] string? Complemento,
    [property: JsonPropertyName("bairro")] string? Bairro,
    [property: JsonPropertyName("cep")] string? Cep,
    [property: JsonPropertyName("codigo_municipio_ibge")] long? CodigoMunicipioIbge,
    [property: JsonPropertyName("uf")] string? Uf,
    [property: JsonPropertyName("ddd_telefone_1")] string? DddTelefone1,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("descricao_situacao_cadastral")] string? DescricaoSituacaoCadastral);

/// <summary>
/// Consulta pública e gratuita (sem chave/autenticação) que agrega
/// dados cadastrais da Receita Federal por CNPJ. Ver ConsultaCnpjResponse
/// pra saber exatamente quais campos são repassados e por quê.
/// </summary>
public sealed class BrasilApiCnpjClient : IConsultarCnpjUseCase
{
    private const string BaseUrl = "https://brasilapi.com.br/api/cnpj/v1/";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BrasilApiCnpjClient> _logger;

    public BrasilApiCnpjClient(IHttpClientFactory httpClientFactory, ILogger<BrasilApiCnpjClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ConsultaCnpjResponse> ExecutarAsync(string cnpj, CancellationToken cancellationToken)
    {
        var cnpjLimpo = new string(cnpj.Where(char.IsDigit).ToArray());
        if (cnpjLimpo.Length != 14)
            throw new RegraNegocioException("CNPJ deve ter 14 dígitos.");

        var client = _httpClientFactory.CreateClient("BrasilApi");
        client.Timeout = TimeSpan.FromSeconds(10);

        HttpResponseMessage resposta;
        try
        {
            resposta = await client.GetAsync(BaseUrl + cnpjLimpo, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Falha de comunicação ao consultar CNPJ {Cnpj} na BrasilAPI.", cnpjLimpo);
            throw new RegraNegocioException("Não foi possível consultar o CNPJ agora (serviço externo indisponível). Preencha os dados manualmente.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RegraNegocioException("A consulta de CNPJ demorou demais pra responder. Preencha os dados manualmente.");
        }

        if (resposta.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new RecursoNaoEncontradoException($"CNPJ {cnpjLimpo} não encontrado na Receita Federal.");

        if (!resposta.IsSuccessStatusCode)
        {
            _logger.LogWarning("BrasilAPI retornou HTTP {StatusCode} ao consultar CNPJ {Cnpj}.", (int)resposta.StatusCode, cnpjLimpo);
            throw new RegraNegocioException("Não foi possível consultar o CNPJ agora (serviço externo indisponível). Preencha os dados manualmente.");
        }

        var corpo = await resposta.Content.ReadAsStringAsync(cancellationToken);
        var dados = JsonSerializer.Deserialize<BrasilApiCnpjResponse>(corpo, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new RegraNegocioException("A Receita Federal retornou uma resposta que não foi possível interpretar.");

        return new ConsultaCnpjResponse(
            Cnpj: cnpjLimpo,
            RazaoSocial: dados.RazaoSocial ?? string.Empty,
            NomeFantasia: dados.NomeFantasia,
            Logradouro: dados.Logradouro,
            Numero: dados.Numero,
            Complemento: dados.Complemento,
            Bairro: dados.Bairro,
            Cep: dados.Cep is null ? null : new string(dados.Cep.Where(char.IsDigit).ToArray()),
            CodigoMunicipio: dados.CodigoMunicipioIbge?.ToString(),
            Uf: dados.Uf,
            Telefone: dados.DddTelefone1,
            Email: dados.Email,
            SituacaoCadastral: dados.DescricaoSituacaoCadastral);
    }
}
