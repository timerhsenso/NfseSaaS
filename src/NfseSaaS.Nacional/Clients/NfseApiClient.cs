using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using NfseSaaS.Nacional.Exceptions;
using NfseSaaS.Nacional.Options;

namespace NfseSaaS.Nacional.Clients;

/// <summary>
/// Implementação HTTP usando IHttpClientFactory (nunca "new HttpClient()"
/// manual — ver Requisito 13). O certificado mTLS é anexado por Empresa
/// através de CertificateHttpMessageHandlerBuilderFilter, disparado pela
/// convenção de nome de SefinNacionalClientNames — ver comentário completo
/// da decisão de arquitetura em CertificateHttpMessageHandlerBuilderFilter.
///
/// A URL base não é configurada via HttpClient.BaseAddress (que exigiria
/// nomes de client conhecidos em tempo de registro); em vez disso, é
/// montada a partir de NfseNacionalOptions a cada chamada, o que também
/// permite trocar o ambiente (homologação/produção) sem recompilar.
/// </summary>
public sealed class NfseApiClient : INfseApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NfseNacionalOptions _options;

    public NfseApiClient(IHttpClientFactory httpClientFactory, IOptions<NfseNacionalOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<(int StatusCode, string Body)> EnviarDpsAsync(Guid empresaId, string dpsXmlGZipBase64, CancellationToken cancellationToken)
    {
        var client = ObterClient(empresaId);
        using var timeoutCts = CriarTokenComTimeout(cancellationToken);

        try
        {
            var payload = new { dpsXmlGZipB64 = dpsXmlGZipBase64 };
            using var response = await client.PostAsJsonAsync(MontarUrl("nfse"), payload, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ((int)response.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            throw new NfseApiException("Falha de comunicação com a SEFIN Nacional ao enviar a DPS.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NfseApiException($"Tempo limite ({_options.TimeoutSeconds}s) excedido ao enviar a DPS à SEFIN Nacional.", ex);
        }
    }

    public async Task<(int StatusCode, string Body)> ConsultarPorChaveAsync(Guid empresaId, string chaveAcesso, CancellationToken cancellationToken)
    {
        var client = ObterClient(empresaId);
        using var timeoutCts = CriarTokenComTimeout(cancellationToken);

        try
        {
            using var response = await client.GetAsync(MontarUrl($"nfse/{chaveAcesso}"), timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ((int)response.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            throw new NfseApiException($"Falha de comunicação com a SEFIN Nacional ao consultar a chave {chaveAcesso}.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NfseApiException($"Tempo limite ({_options.TimeoutSeconds}s) excedido ao consultar a chave {chaveAcesso}.", ex);
        }
    }

    public async Task<(int StatusCode, string Body)> EnviarEventoAsync(Guid empresaId, string chaveAcesso, string eventoXmlGZipBase64, CancellationToken cancellationToken)
    {
        var client = ObterClient(empresaId);
        using var timeoutCts = CriarTokenComTimeout(cancellationToken);

        try
        {
            // Nome do campo confirmado no Swagger oficial da SEFIN Nacional
            // (schema EventosPostRequest): "pedidoRegistroEventoXmlGZipB64".
            var payload = new { pedidoRegistroEventoXmlGZipB64 = eventoXmlGZipBase64 };
            using var response = await client.PostAsJsonAsync(MontarUrl($"nfse/{chaveAcesso}/eventos"), payload, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ((int)response.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            throw new NfseApiException($"Falha de comunicação com a SEFIN Nacional ao enviar o evento da chave {chaveAcesso}.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NfseApiException($"Tempo limite ({_options.TimeoutSeconds}s) excedido ao enviar o evento da chave {chaveAcesso}.", ex);
        }
    }

    private HttpClient ObterClient(Guid empresaId) =>
        _httpClientFactory.CreateClient(SefinNacionalClientNames.ParaEmpresa(empresaId));

    private Uri MontarUrl(string caminhoRelativo)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            throw new NfseApiException("NfseNacional:BaseUrl não configurada.");

        var baseUrl = _options.BaseUrl.EndsWith('/') ? _options.BaseUrl : _options.BaseUrl + "/";
        return new Uri(new Uri(baseUrl), caminhoRelativo);
    }

    private CancellationTokenSource CriarTokenComTimeout(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        return cts;
    }
}
