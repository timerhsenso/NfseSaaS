using System.Text.Json;
using NfseSaaS.Nacional.Models;

namespace NfseSaaS.Nacional.Clients;

/// <summary>
/// Leitura da resposta de GET /DFe/{nsu} do ADN, sem HTTP (testável isolada):
/// - 2xx: lote de documentos;
/// - 404 com StatusProcessamento NENHUM_DOCUMENTO_LOCALIZADO: fila vazia,
///   não é erro (não há nota nova depois do NSU informado);
/// - qualquer outro: recusa, com os erros que o ADN informar (podem vir
///   como texto ou como objeto { Codigo, Descricao, Complemento }; e às
///   vezes vêm vazios, como na REJEICAO de "InscricaoCertificado" vista
///   em homologação em 07/11/2026).
/// </summary>
public static class RespostaAdn
{
    public const string NenhumDocumentoLocalizado = "NENHUM_DOCUMENTO_LOCALIZADO";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Resultado: o lote (sucesso ou fila vazia) OU a recusa com o motivo (nulo quando o ADN não diz).</summary>
    public sealed record Leitura(DfeLoteResponse? Lote, bool Recusada, string? Motivo);

    public static Leitura Interpretar(int statusHttp, string corpo)
    {
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(corpo) ? "{}" : corpo); }
        catch (JsonException) { /* corpo não-JSON: trata abaixo */ }

        using (doc)
        {
            var raiz = doc?.RootElement;
            var status = raiz is { ValueKind: JsonValueKind.Object } r ? Texto(r, "StatusProcessamento") : null;

            if (statusHttp is >= 200 and < 300 && raiz is { ValueKind: JsonValueKind.Object } ok)
                return new Leitura(LerLote(ok, status), false, null);

            if (statusHttp == 404 && string.Equals(status, NenhumDocumentoLocalizado, StringComparison.OrdinalIgnoreCase))
                return new Leitura(Vazio(status!), false, null);

            var motivo = raiz is { ValueKind: JsonValueKind.Object } erro ? Erros(erro) : null;
            return new Leitura(null, true, motivo);
        }
    }

    private static DfeLoteResponse LerLote(JsonElement raiz, string? status)
    {
        var itens = raiz.TryGetProperty("LoteDFe", out var lote) && lote.ValueKind == JsonValueKind.Array
            ? lote.Deserialize<List<DfeItem>>(Json) ?? []
            : [];
        return new DfeLoteResponse(
            status ?? "",
            itens,
            ListaDeTextos(raiz, "Alertas"),
            ListaDeTextos(raiz, "Erros"),
            Texto(raiz, "TipoAmbiente") ?? "",
            Texto(raiz, "VersaoAplicativo") ?? "",
            raiz.TryGetProperty("DataHoraProcessamento", out var dh) && dh.TryGetDateTimeOffset(out var quando) ? quando : DateTimeOffset.MinValue);
    }

    private static DfeLoteResponse Vazio(string status) =>
        new(status, [], [], [], "", "", DateTimeOffset.MinValue);

    /// <summary>Erros do ADN em texto: "E2243 - descrição (complemento)"; nulo quando não há nenhum.</summary>
    private static string? Erros(JsonElement raiz)
    {
        var erros = ListaDeTextos(raiz, "Erros");
        return erros.Count == 0 ? null : string.Join("; ", erros);
    }

    private static List<string> ListaDeTextos(JsonElement raiz, string nome)
    {
        if (!Propriedade(raiz, nome, out var lista) || lista.ValueKind != JsonValueKind.Array)
            return [];

        var textos = new List<string>();
        foreach (var item in lista.EnumerateArray())
        {
            var texto = item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Object => DeObjeto(item),
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(texto))
                textos.Add(texto.Trim());
        }
        return textos;
    }

    private static string? DeObjeto(JsonElement item)
    {
        var codigo = Texto(item, "Codigo");
        var descricao = Texto(item, "Descricao");
        var complemento = Texto(item, "Complemento");
        var texto = string.Join(" - ", new[] { codigo, descricao }.Where(t => !string.IsNullOrWhiteSpace(t)));
        if (!string.IsNullOrWhiteSpace(complemento))
            texto = string.IsNullOrEmpty(texto) ? complemento : $"{texto} ({complemento})";
        return string.IsNullOrWhiteSpace(texto) ? null : texto;
    }

    private static string? Texto(JsonElement objeto, string nome) =>
        Propriedade(objeto, nome, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

    /// <summary>Propriedade sem diferenciar maiúsculas (o ADN usa PascalCase, mas não confiamos nisso).</summary>
    private static bool Propriedade(JsonElement objeto, string nome, out JsonElement valor)
    {
        foreach (var p in objeto.EnumerateObject())
            if (string.Equals(p.Name, nome, StringComparison.OrdinalIgnoreCase))
            {
                valor = p.Value;
                return true;
            }
        valor = default;
        return false;
    }
}
