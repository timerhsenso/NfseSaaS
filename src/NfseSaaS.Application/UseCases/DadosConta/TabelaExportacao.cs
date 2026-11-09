using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NfseSaaS.Application.UseCases.DadosConta;

/// <summary>
/// Uma tabela da exportação (ex.: clientes), gravada em DOIS formatos:
/// - CSV para abrir no Excel: separador ";", UTF-8 com BOM, número com
///   vírgula decimal, data dd/MM/aaaa, data e hora em Brasília, Sim/Não;
/// - JSON para levar a outro sistema: valores com tipo (número, true/false,
///   data ISO 8601).
/// Os valores aceitos são texto, números, bool, Guid, DateOnly,
/// DateTimeOffset e enums (gravados pelo nome).
/// </summary>
public sealed class TabelaExportacao
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly TimeSpan OffsetBrasilia = TimeSpan.FromHours(-3);

    public TabelaExportacao(string nome, params string[] colunas)
    {
        if (colunas.Length == 0)
            throw new ArgumentException("A tabela precisa de colunas.", nameof(colunas));
        Nome = nome;
        Colunas = colunas;
    }

    /// <summary>Nome do arquivo sem extensão (ex.: "clientes").</summary>
    public string Nome { get; }

    public IReadOnlyList<string> Colunas { get; }

    public List<object?[]> Linhas { get; } = [];

    public void Adicionar(params object?[] valores)
    {
        if (valores.Length != Colunas.Count)
            throw new ArgumentException($"A tabela {Nome} tem {Colunas.Count} colunas e recebeu {valores.Length} valores.", nameof(valores));
        Linhas.Add(valores);
    }

    public byte[] ParaCsv()
    {
        var sb = new StringBuilder();
        sb.AppendJoin(';', Colunas.Select(EscaparCsv)).Append("\r\n");
        foreach (var linha in Linhas)
            sb.AppendJoin(';', linha.Select(v => EscaparCsv(TextoCsv(v)))).Append("\r\n");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public byte[] ParaJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartArray();
            foreach (var linha in Linhas)
            {
                w.WriteStartObject();
                for (var i = 0; i < Colunas.Count; i++)
                {
                    w.WritePropertyName(Colunas[i]);
                    EscreverJson(w, linha[i]);
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        return ms.ToArray();
    }

    internal static string TextoCsv(object? valor) => valor switch
    {
        null => string.Empty,
        string s => s,
        bool b => b ? "Sim" : "Não",
        decimal d => d.ToString("0.00##", PtBr),
        double d => d.ToString(PtBr),
        DateOnly d => d.ToString("dd/MM/yyyy", PtBr),
        DateTimeOffset d => d.ToOffset(OffsetBrasilia).ToString("dd/MM/yyyy HH:mm", PtBr),
        Enum e => e.ToString(),
        IFormattable f => f.ToString(null, PtBr),
        _ => valor.ToString() ?? string.Empty
    };

    /// <summary>Aspas quando precisa (separador, aspas, quebra de linha) e proteção contra fórmula no Excel.</summary>
    internal static string EscaparCsv(string texto)
    {
        // "=", "+", "-", "@" no começo viram fórmula no Excel (injeção de CSV).
        // Número negativo ("-12,50") é legítimo e não é texto livre: fica.
        if (texto.Length > 0 && texto[0] is '=' or '+' or '@' or '\t' or '\r'
            || (texto.Length > 0 && texto[0] == '-' && !decimal.TryParse(texto, NumberStyles.Number, PtBr, out _)))
            texto = "'" + texto;

        return texto.IndexOfAny([';', '"', '\r', '\n']) >= 0
            ? "\"" + texto.Replace("\"", "\"\"") + "\""
            : texto;
    }

    private static void EscreverJson(Utf8JsonWriter w, object? valor)
    {
        switch (valor)
        {
            case null: w.WriteNullValue(); break;
            case string s: w.WriteStringValue(s); break;
            case bool b: w.WriteBooleanValue(b); break;
            case decimal d: w.WriteNumberValue(d); break;
            case int i: w.WriteNumberValue(i); break;
            case long l: w.WriteNumberValue(l); break;
            case double d: w.WriteNumberValue(d); break;
            case Guid g: w.WriteStringValue(g); break;
            case DateOnly d: w.WriteStringValue(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); break;
            case DateTimeOffset d: w.WriteStringValue(d); break;
            case Enum e: w.WriteStringValue(e.ToString()); break;
            default: w.WriteStringValue(Convert.ToString(valor, CultureInfo.InvariantCulture)); break;
        }
    }
}
