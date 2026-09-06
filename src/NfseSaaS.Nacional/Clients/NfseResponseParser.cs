using System.Text.Json;
using NfseSaaS.Nacional.Responses;

namespace NfseSaaS.Nacional.Clients;

/// <summary>
/// Desserializa o corpo JSON retornado pela SEFIN Nacional (sucesso ou
/// rejeição) para NfseNacionalResponse. Puro parsing/mapeamento — sem
/// regra fiscal — espelha o que já foi observado e validado na POC.
/// </summary>
public static class NfseResponseParser
{
    public static NfseNacionalResponse Parse(int statusCode, string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var sucesso = statusCode is >= 200 and < 300;

        int? tipoAmbiente = root.TryGetProperty("tipoAmbiente", out var tipoAmbienteEl)
            ? tipoAmbienteEl.GetInt32()
            : null;

        string? versaoAplicativo = root.TryGetProperty("versaoAplicativo", out var versaoEl)
            ? versaoEl.GetString()
            : null;

        DateTimeOffset? dataHoraProcessamento = root.TryGetProperty("dataHoraProcessamento", out var dataEl)
            && dataEl.TryGetDateTimeOffset(out var dataValor)
            ? dataValor
            : null;

        string? idDps = root.TryGetProperty("idDps", out var idDpsEl)
            ? idDpsEl.GetString()
            : root.TryGetProperty("idDPS", out var idDpsEl2)
                ? idDpsEl2.GetString()
                : null;

        string? chaveAcesso = root.TryGetProperty("chaveAcesso", out var chaveEl)
            ? chaveEl.GetString()
            : null;

        string? nfseXmlGZipB64 = root.TryGetProperty("nfseXmlGZipB64", out var xmlEl)
            ? xmlEl.GetString()
            : null;

        var erros = new List<NfseErro>();
        if (root.TryGetProperty("erros", out var errosEl) && errosEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var erro in errosEl.EnumerateArray())
            {
                var codigo = erro.TryGetProperty("Codigo", out var codigoEl) ? codigoEl.GetString() ?? "" : "";
                var descricao = erro.TryGetProperty("Descricao", out var descricaoEl) ? descricaoEl.GetString() ?? "" : "";
                erros.Add(new NfseErro(codigo, descricao));
            }
        }

        return new NfseNacionalResponse(
            sucesso,
            tipoAmbiente,
            versaoAplicativo,
            dataHoraProcessamento,
            idDps,
            chaveAcesso,
            nfseXmlGZipB64,
            erros);
    }
}
