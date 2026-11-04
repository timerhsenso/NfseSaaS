using Polly;
using Polly.Extensions.Http;

namespace NfseSaaS.Nacional.Resilience;

/// <summary>
/// Políticas Polly compartilhadas por TODO client HTTP criado através do
/// IHttpClientFactory neste módulo (NfseApiClient e AdnDistribuicaoClient
/// — ambos usam clients com nome dinâmico por Empresa, então a política
/// é aplicada via ConfigureHttpClientDefaults em DependencyInjection.cs,
/// não por client nomeado individualmente).
///
/// Composição: CircuitBreaker por FORA, Retry por DENTRO
/// (CriarCircuitBreaker().WrapAsync(CriarRetry())). Isso importa: assim,
/// o circuit breaker só conta como "1 falha" uma chamada que já esgotou
/// todas as tentativas de retry — e não cada tentativa individual, o que
/// abriria o circuito prematuramente por ruído.
///
/// O retry só é aplicado a GET (ver DependencyInjection.cs): envio de DPS e
/// de evento (POST) passa só pelo circuit breaker, sem reenvio automático.
/// </summary>
public static class PollyPolicies
{
    /// <summary>
    /// Marca uma requisição para passar FORA da resiliência global
    /// (ConfigureHttpClientDefaults pega todo HttpClient do container). Usado
    /// por chamadas que não têm nada a ver com a SEFIN — ex.: importação de
    /// índices do Banco Central: sem isto, o Banco Central fora do ar abriria
    /// o circuit breaker COMPARTILHADO e bloquearia a emissão de notas por 30s.
    /// Uso: <c>request.Options.Set(PollyPolicies.IgnorarResilienciaGlobal, true)</c>.
    /// </summary>
    public static readonly HttpRequestOptionsKey<bool> IgnorarResilienciaGlobal = new("NfseSaaS.IgnorarResilienciaGlobal");

    private static readonly IAsyncPolicy<HttpResponseMessage> SemPolitica = Policy.NoOpAsync<HttpResponseMessage>();

    /// <summary>Política para a requisição: nenhuma se marcada com <see cref="IgnorarResilienciaGlobal"/>; senão retry+breaker em GET, só breaker no resto.</summary>
    public static IAsyncPolicy<HttpResponseMessage> Selecionar(
        HttpRequestMessage request, IAsyncPolicy<HttpResponseMessage> comRetry, IAsyncPolicy<HttpResponseMessage> circuitBreaker)
    {
        if (request.Options.TryGetValue(IgnorarResilienciaGlobal, out var ignorar) && ignorar)
            return SemPolitica;

        return request.Method == HttpMethod.Get ? comRetry : circuitBreaker;
    }

    /// <summary>
    /// Retry com backoff exponencial (1s, 2s, 4s) — só para falha
    /// TRANSITÓRIA: erro de rede (HttpRequestException) ou HTTP 5xx/408
    /// da própria SEFIN/ADN. NUNCA em 4xx: um 4xx é uma rejeição de
    /// negócio (DPS inválida, autenticação, etc.) — reenviar o MESMO
    /// payload não muda o resultado, e no caso do envio de DPS (POST) é
    /// um risco reenviar sem necessidade.
    ///
    /// O DPS Id é determinístico (calculado a partir de Empresa+série+
    /// número) — o próprio padrão nacional é desenhado pra tolerar um
    /// reenvio acidental depois de a SEFIN já ter processado o pedido
    /// (ela reconhece o mesmo Id) — mas ainda assim só faz sentido
    /// reenviar quando o problema foi de fato transitório, nunca depois
    /// de uma rejeição definitiva.
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> CriarRetry() =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(3, tentativa => TimeSpan.FromSeconds(Math.Pow(2, tentativa - 1)));

    /// <summary>
    /// Depois de 5 falhas consecutivas (já contando só a falha FINAL de
    /// cada chamada — ver composição acima), para de tentar por 30s e
    /// falha rápido (BrokenCircuitException, mapeada em NfseApiClient/
    /// AdnDistribuicaoClient junto com HttpRequestException).
    ///
    /// Sem isto, uma instabilidade real da SEFIN durante uma Nota Mensal
    /// em lote (que processa Contratos SEQUENCIALMENTE, ver
    /// EmitirNotaMensalLoteUseCase) faz cada nota da fila esperar os 3
    /// retries + timeout completo antes de desistir — minutos perdidos
    /// multiplicados pelo tamanho do lote. Compartilhado entre TODAS as
    /// Empresas de propósito: se a SEFIN está fora, está fora pra todo
    /// mundo, então o circuito abre uma vez só, não por Empresa.
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> CriarCircuitBreaker() =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
}
