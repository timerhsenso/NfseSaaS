using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Nacional.Resilience;

namespace NfseSaaS.Infrastructure.IndicesEconomicos;

/// <summary>
/// Cliente da API SGS do Banco Central:
/// GET {BaseUrl}bcdata.sgs.{codigo}/dados?formato=json&amp;dataInicial=dd/MM/yyyy&amp;dataFinal=dd/MM/yyyy
/// → [{"data":"01/10/2026","valor":"0.09"}, ...]
///
/// FORA da resiliência global de propósito (PollyPolicies.IgnorarResilienciaGlobal):
/// o circuit breaker é compartilhado com a SEFIN, e o Banco Central fora do
/// ar não pode bloquear a emissão de notas. Também não precisa de retry:
/// o job diário tem retentativa do Hangfire, e o botão "Importar agora"
/// mostra o erro na hora em vez de segurar a tela por segundos.
/// </summary>
public sealed class BancoCentralSgsClient : IFonteIndicesEconomicos
{
    private readonly HttpClient _http;
    private readonly IndicesEconomicosOptions _options;

    public BancoCentralSgsClient(HttpClient http, IOptions<IndicesEconomicosOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public IReadOnlyCollection<IndiceReajusteContrato> IndicesSuportados =>
        Enum.GetValues<IndiceReajusteContrato>()
            .Where(i => CodigoSerie(i) is not null)
            .ToList();

    public string? DescreverSerie(IndiceReajusteContrato indice) =>
        CodigoSerie(indice) is { } codigo ? $"SGS {codigo}" : null;

    public async Task<IReadOnlyList<VariacaoMensalIndice>> ObterVariacoesAsync(
        IndiceReajusteContrato indice, DateOnly inicio, DateOnly fim, CancellationToken cancellationToken)
    {
        var codigo = CodigoSerie(indice)
            ?? throw new InvalidOperationException($"Índice {indice} não tem série configurada no Banco Central.");

        var primeiroDia = new DateOnly(inicio.Year, inicio.Month, 1);
        var ultimoDia = new DateOnly(fim.Year, fim.Month, 1).AddMonths(1).AddDays(-1);
        if (ultimoDia < primeiroDia)
            return Array.Empty<VariacaoMensalIndice>();

        var url = $"{_options.BaseUrl.TrimEnd('/')}/bcdata.sgs.{codigo}/dados?formato=json" +
                  $"&dataInicial={primeiroDia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}" +
                  $"&dataFinal={ultimoDia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";

        using var requisicao = new HttpRequestMessage(HttpMethod.Get, url);
        requisicao.Options.Set(PollyPolicies.IgnorarResilienciaGlobal, true);
        using var resposta = await _http.SendAsync(requisicao, cancellationToken);

        // O SGS responde 404 quando não há nenhum valor no período (ex.: mês
        // recém-encerrado ainda não publicado) — não é erro.
        if (resposta.StatusCode == HttpStatusCode.NotFound)
            return Array.Empty<VariacaoMensalIndice>();

        var corpo = await resposta.Content.ReadAsStringAsync(cancellationToken);
        if (!resposta.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Banco Central (SGS {codigo}) respondeu {(int)resposta.StatusCode}: {Resumir(corpo)}",
                null, resposta.StatusCode);

        return Interpretar(corpo, codigo);
    }

    /// <summary>Converte o JSON do SGS. Público para teste.</summary>
    public static IReadOnlyList<VariacaoMensalIndice> Interpretar(string json, int codigo)
    {
        List<PontoSgs>? pontos;
        try
        {
            pontos = JsonSerializer.Deserialize<List<PontoSgs>>(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Resposta inesperada do Banco Central (SGS {codigo}): {Resumir(json)}", ex);
        }

        var resultado = new Dictionary<DateOnly, decimal>();
        foreach (var ponto in pontos ?? new List<PontoSgs>())
        {
            // O SGS às vezes devolve "valor" vazio para um mês sem dado.
            if (string.IsNullOrWhiteSpace(ponto.Data) || string.IsNullOrWhiteSpace(ponto.Valor))
                continue;

            if (!DateOnly.TryParseExact(ponto.Data, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
                throw new InvalidOperationException($"Data inválida na resposta do Banco Central (SGS {codigo}): \"{ponto.Data}\".");

            if (!decimal.TryParse(ponto.Valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor))
                throw new InvalidOperationException($"Valor inválido na resposta do Banco Central (SGS {codigo}): \"{ponto.Valor}\".");

            // Série mensal: a data é sempre dia 1. Normaliza mesmo assim, e
            // o último valor de um mês prevalece se vier repetido.
            resultado[new DateOnly(data.Year, data.Month, 1)] = decimal.Round(valor, 4, MidpointRounding.AwayFromZero);
        }

        return resultado
            .OrderBy(p => p.Key)
            .Select(p => new VariacaoMensalIndice(p.Key, p.Value))
            .ToList();
    }

    private int? CodigoSerie(IndiceReajusteContrato indice) =>
        indice != IndiceReajusteContrato.Outro && _options.Series.TryGetValue(indice.ToString(), out var codigo) && codigo > 0
            ? codigo
            : null;

    private static string Resumir(string texto) =>
        texto.Length <= 200 ? texto : texto[..200] + "…";

    private sealed class PontoSgs
    {
        [JsonPropertyName("data")]
        public string? Data { get; set; }

        [JsonPropertyName("valor")]
        public string? Valor { get; set; }
    }
}
