using System.Text.Json;
using NfseSaaS.Nacional.Responses;

namespace NfseSaaS.Nacional.Clients;

/// <summary>
/// Desserializa o corpo JSON retornado pela SEFIN Nacional no endpoint de
/// eventos (sucesso ou erro) para EventoNacionalResponse. Formato-alvo
/// confirmado contra o Swagger oficial da SEFIN Nacional
/// (EventosPostResponseSucesso / ResponseErro: objeto único "erro", não
/// lista "erros" como no endpoint /nfse principal) — mas NUNCA testado
/// contra uma rejeição real (o cancelamento em si ainda não tinha sido
/// exercitado neste projeto). Por isso, em vez de silenciosamente não
/// achar nada e devolver um erro vazio quando o formato real vier
/// diferente do esperado, este parser tenta, em ordem:
/// 1) o formato documentado (objeto "erro" com codigo/descricao/complemento);
/// 2) o formato do endpoint /nfse principal (lista "erros" em PascalCase),
///    caso a SEFIN reaproveite o mesmo formato de erro nos dois endpoints;
/// 3) o corpo bruto da resposta como descrição — pra NUNCA devolver um
///    "erro sem motivo" quando na verdade a SEFIN mandou um motivo, só
///    que num formato que este parser ainda não conhecia.
/// </summary>
public static class EventoResponseParser
{
    public static EventoNacionalResponse Parse(int statusCode, string body)
    {
        var sucesso = statusCode is >= 200 and < 300;

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(body);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Corpo não é JSON (ex.: página de erro de um proxy/gateway
            // intermediário) — não há nada estruturado pra extrair, mas o
            // corpo bruto ainda é melhor que "sem motivo nenhum".
            return new EventoNacionalResponse(
                sucesso, null, null, null, null,
                sucesso ? null : new NfseMensagemProcessamento(statusCode.ToString(), TruncarCorpo(body), null));
        }

        int? tipoAmbiente = root.TryGetProperty("tipoAmbiente", out var tipoAmbienteEl) && tipoAmbienteEl.ValueKind == JsonValueKind.Number
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

        if (sucesso)
            return new EventoNacionalResponse(true, tipoAmbiente, versaoAplicativo, dataHoraProcessamento, eventoXmlGZipB64, null);

        var erro = ExtrairErroFormatoDocumentado(root) ?? ExtrairErroFormatoListaPascalCase(root);

        // Nem o formato documentado nem o formato alternativo acharam
        // nada — melhor mostrar o corpo bruto do que "motivo não
        // informado" (que parece um erro de negócio real, mas não é).
        erro ??= new NfseMensagemProcessamento(statusCode.ToString(), TruncarCorpo(body), null);

        return new EventoNacionalResponse(false, tipoAmbiente, versaoAplicativo, dataHoraProcessamento, eventoXmlGZipB64, erro);
    }

    private static NfseMensagemProcessamento? ExtrairErroFormatoDocumentado(JsonElement root)
    {
        if (!root.TryGetProperty("erro", out var erroEl) || erroEl.ValueKind != JsonValueKind.Object)
            return null;

        var codigo = erroEl.TryGetProperty("codigo", out var codigoEl) ? codigoEl.GetString() : null;
        var descricao = erroEl.TryGetProperty("descricao", out var descricaoEl) ? descricaoEl.GetString() : null;
        var complemento = erroEl.TryGetProperty("complemento", out var complementoEl) ? complementoEl.GetString() : null;

        return string.IsNullOrWhiteSpace(codigo) && string.IsNullOrWhiteSpace(descricao)
            ? null
            : new NfseMensagemProcessamento(codigo, descricao, complemento);
    }

    /// <summary>Mesmo formato usado pelo NfseResponseParser (endpoint /nfse) — lista "erros" com "Codigo"/"Descricao" em PascalCase.</summary>
    private static NfseMensagemProcessamento? ExtrairErroFormatoListaPascalCase(JsonElement root)
    {
        if (!root.TryGetProperty("erros", out var errosEl) || errosEl.ValueKind != JsonValueKind.Array)
            return null;

        var mensagens = new List<string>();
        string? primeiroCodigo = null;

        foreach (var item in errosEl.EnumerateArray())
        {
            var codigo = item.TryGetProperty("Codigo", out var codigoEl) ? codigoEl.GetString() : null;
            var descricao = item.TryGetProperty("Descricao", out var descricaoEl) ? descricaoEl.GetString() : null;

            primeiroCodigo ??= codigo;
            if (!string.IsNullOrWhiteSpace(descricao))
                mensagens.Add(descricao);
        }

        return mensagens.Count == 0 ? null : new NfseMensagemProcessamento(primeiroCodigo, string.Join(" | ", mensagens), null);
    }

    private static string TruncarCorpo(string body)
    {
        const int limite = 500;
        return body.Length > limite ? body[..limite] + "..." : body;
    }
}
