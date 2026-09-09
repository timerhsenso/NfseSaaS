using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using NfseSaaS.Nacional.Exceptions;
using NfseSaaS.Nacional.Models;
using NfseSaaS.Nacional.Options;

namespace NfseSaaS.Nacional.Clients;

/// <summary>
/// Reaproveita deliberadamente o MESMO client HTTP nomeado por Empresa
/// (SefinNacionalClientNames.ParaEmpresa) que o SEFIN de emissão usa — o
/// filtro que anexa o certificado mTLS (CertificateHttpMessageHandlerBuilderFilter)
/// só olha o NOME do client, não a URL chamada nele. Como o certificado é
/// o mesmo (por Empresa) tanto pra emissão quanto pra distribuição de
/// DF-e, não há necessidade de uma convenção de nome de client separada —
/// só aponta pra uma URL absoluta diferente (host do ADN) na mesma
/// chamada GetAsync.
/// </summary>
public sealed class AdnDistribuicaoClient : IAdnDistribuicaoClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AdnOptions _options;

    public AdnDistribuicaoClient(IHttpClientFactory httpClientFactory, IOptions<AdnOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<DfeLoteResponse> ConsultarPorNsuAsync(Guid empresaId, long ultimoNsuProcessado, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            throw new NfseApiException("Adn:BaseUrl não configurada.");

        var client = _httpClientFactory.CreateClient(SefinNacionalClientNames.ParaEmpresa(empresaId));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        var baseUrl = _options.BaseUrl.EndsWith('/') ? _options.BaseUrl : _options.BaseUrl + "/";
        var url = new Uri(new Uri(baseUrl), $"DFe/{ultimoNsuProcessado}");

        try
        {
            using var response = await client.GetAsync(url, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new NfseApiException($"ADN retornou HTTP {(int)response.StatusCode} ao consultar NSU {ultimoNsuProcessado}: {body}");

            var resultado = System.Text.Json.JsonSerializer.Deserialize<DfeLoteResponse>(body,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return resultado ?? throw new NfseApiException("ADN retornou corpo vazio/inválido.");
        }
        catch (HttpRequestException ex)
        {
            throw new NfseApiException($"Falha de comunicação com o ADN ao consultar NSU {ultimoNsuProcessado}.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NfseApiException($"Tempo limite ({_options.TimeoutSeconds}s) excedido ao consultar o ADN.", ex);
        }
    }
}
