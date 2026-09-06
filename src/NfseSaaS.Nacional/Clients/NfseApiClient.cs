using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using NfseSaaS.Nacional.Exceptions;
using NfseSaaS.Nacional.Options;

namespace NfseSaaS.Nacional.Clients;

/// <summary>
/// Implementação HTTP usando IHttpClientFactory (nunca "new HttpClient()"
/// manual — ver Requisito 13). BaseAddress, timeout e headers vêm de
/// NfseNacionalOptions.
///
/// NOTA IMPORTANTE (Fase 1): o certificado cliente para mTLS ainda NÃO está
/// incorporado aqui — conforme Requisito 13/11 da especificação, isso será
/// feito na Fase 2 através de ICertificateProvider, quando a origem real do
/// certificado (arquivo, Key Vault, etc.) for definida. A POC já validou
/// que o fluxo completo com mTLS funciona (HTTP 201 obtido); falta apenas
/// conectar esse client à abstração de certificado.
/// </summary>
public sealed class NfseApiClient : INfseApiClient
{
    public const string HttpClientName = "SefinNacional";

    private readonly HttpClient _httpClient;

    public NfseApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
    }

    public async Task<(int StatusCode, string Body)> EnviarDpsAsync(string dpsXmlGZipBase64, CancellationToken cancellationToken)
    {
        try
        {
            var payload = new { dpsXmlGZipB64 = dpsXmlGZipBase64 };
            using var response = await _httpClient.PostAsJsonAsync("nfse", payload, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ((int)response.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            throw new NfseApiException("Falha de comunicação com a SEFIN Nacional ao enviar a DPS.", ex);
        }
    }

    public async Task<(int StatusCode, string Body)> ConsultarPorChaveAsync(string chaveAcesso, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"nfse/{chaveAcesso}", cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ((int)response.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            throw new NfseApiException($"Falha de comunicação com a SEFIN Nacional ao consultar a chave {chaveAcesso}.", ex);
        }
    }
}
