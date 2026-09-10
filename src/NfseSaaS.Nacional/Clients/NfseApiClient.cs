using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
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
/// A URL base é resolvida A CADA CHAMADA a partir de NfseNacionalOptions
/// E do tpAmb informado pelo chamador — NfseNacionalOptions.ObterBaseUrl
/// escolhe entre BaseUrlHomologacao/BaseUrlProducao. Este client não
/// decide sozinho qual ambiente usar; só executa o que recebeu.
/// </summary>
public sealed class NfseApiClient : INfseApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NfseNacionalOptions _options;
    private readonly ILogger<NfseApiClient> _logger;

    public NfseApiClient(IHttpClientFactory httpClientFactory, IOptions<NfseNacionalOptions> options, ILogger<NfseApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<(int StatusCode, string Body)> EnviarDpsAsync(Guid empresaId, string tpAmb, string dpsXmlGZipBase64, CancellationToken cancellationToken)
    {
        var client = ObterClient(empresaId);
        using var timeoutCts = CriarTokenComTimeout(cancellationToken);

        try
        {
            var payload = new { dpsXmlGZipB64 = dpsXmlGZipBase64 };
            using var response = await client.PostAsJsonAsync(MontarUrl("nfse", tpAmb), payload, timeoutCts.Token);
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

    public async Task<(int StatusCode, string Body)> ConsultarPorChaveAsync(Guid empresaId, string tpAmb, string chaveAcesso, CancellationToken cancellationToken)
    {
        var client = ObterClient(empresaId);
        using var timeoutCts = CriarTokenComTimeout(cancellationToken);

        try
        {
            using var response = await client.GetAsync(MontarUrl($"nfse/{chaveAcesso}", tpAmb), timeoutCts.Token);
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

    public async Task<(int StatusCode, string Body)> EnviarEventoAsync(Guid empresaId, string tpAmb, string chaveAcesso, string eventoXmlGZipBase64, CancellationToken cancellationToken)
    {
        var client = ObterClient(empresaId);
        using var timeoutCts = CriarTokenComTimeout(cancellationToken);

        try
        {
            // Nome do campo confirmado no Swagger oficial da SEFIN Nacional
            // (schema EventosPostRequest): "pedidoRegistroEventoXmlGZipB64".
            var payload = new { pedidoRegistroEventoXmlGZipB64 = eventoXmlGZipBase64 };
            using var response = await client.PostAsJsonAsync(MontarUrl($"nfse/{chaveAcesso}/eventos", tpAmb), payload, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Este endpoint ainda não tinha sido exercitado contra uma
                // rejeição real quando foi implementado — logar o corpo
                // bruto aqui é o que permite confirmar/corrigir o formato
                // de erro assumido em EventoResponseParser, em vez de
                // ficar só com "motivo não informado" na tela.
                _logger.LogWarning(
                    "SEFIN Nacional retornou HTTP {StatusCode} ao registrar evento da chave {ChaveAcesso}. Corpo bruto: {Body}",
                    (int)response.StatusCode, chaveAcesso, body);
            }

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

    public async Task<(int StatusCode, byte[] Bytes, string? ContentType)> ObterDanfsePdfAsync(Guid empresaId, string tpAmb, string chaveAcesso, CancellationToken cancellationToken)
    {
        var client = ObterClient(empresaId);
        using var timeoutCts = CriarTokenComTimeout(cancellationToken);

        try
        {
            using var response = await client.GetAsync(MontarUrl($"danfse/{chaveAcesso}", tpAmb), timeoutCts.Token);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return ((int)response.StatusCode, bytes, response.Content.Headers.ContentType?.MediaType);
        }
        catch (HttpRequestException ex)
        {
            throw new NfseApiException($"Falha de comunicação com a SEFIN Nacional ao obter o DANFSe da chave {chaveAcesso}.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NfseApiException($"Tempo limite ({_options.TimeoutSeconds}s) excedido ao obter o DANFSe da chave {chaveAcesso}.", ex);
        }
    }

    private HttpClient ObterClient(Guid empresaId) =>
        _httpClientFactory.CreateClient(SefinNacionalClientNames.ParaEmpresa(empresaId));

    private Uri MontarUrl(string caminhoRelativo, string tpAmb)
    {
        var baseUrlConfigurada = _options.ObterBaseUrl(tpAmb);

        if (string.IsNullOrWhiteSpace(baseUrlConfigurada))
        {
            var nomeCampo = tpAmb == "1" ? "BaseUrlProducao" : "BaseUrlHomologacao";
            throw new NfseApiException($"NfseNacional:{nomeCampo} não configurada.");
        }

        var baseUrl = baseUrlConfigurada.EndsWith('/') ? baseUrlConfigurada : baseUrlConfigurada + "/";
        return new Uri(new Uri(baseUrl), caminhoRelativo);
    }

    private CancellationTokenSource CriarTokenComTimeout(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        return cts;
    }
}
