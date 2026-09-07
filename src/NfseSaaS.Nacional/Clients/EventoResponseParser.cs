using System.Text.Json;
using NfseSaaS.Nacional.Responses;

namespace NfseSaaS.Nacional.Clients;

/// <summary>
/// Desserializa o corpo JSON retornado pela SEFIN Nacional no endpoint de
/// eventos (sucesso ou erro) para EventoNacionalResponse. Schema
/// confirmado contra o Swagger oficial da SEFIN Nacional
/// (EventosPostResponseSucesso / ResponseErro) — note que aqui o erro vem
/// como objeto único "erro", não como lista "erros" (diferente do
/// NfseResponseParser, usado no endpoint /nfse principal).
/// </summary>
public static class EventoResponseParser
{
    public static EventoNacionalResponse Parse(int statusCode, string body)
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

        string? eventoXmlGZipB64 = root.TryGetProperty("eventoXmlGZipB64", out var xmlEl)
            ? xmlEl.GetString()
            : null;

        NfseMensagemProcessamento? erro = null;
        if (root.TryGetProperty("erro", out var erroEl) && erroEl.ValueKind == JsonValueKind.Object)
        {
            erro = new NfseMensagemProcessamento(
                erroEl.TryGetProperty("codigo", out var codigoEl) ? codigoEl.GetString() : null,
                erroEl.TryGetProperty("descricao", out var descricaoEl) ? descricaoEl.GetString() : null,
                erroEl.TryGetProperty("complemento", out var complementoEl) ? complementoEl.GetString() : null);
        }

        return new EventoNacionalResponse(sucesso, tipoAmbiente, versaoAplicativo, dataHoraProcessamento, eventoXmlGZipB64, erro);
    }
}
