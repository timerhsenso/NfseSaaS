using System.Globalization;
using System.Text.RegularExpressions;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Regras do número do contrato (texto livre, único por empresa). O padrão
/// proposto é "ano/sequência" da empresa (2026/0001): curto, ordena
/// sozinho e diz quantos contratos saíram no ano. Quem já tem numeração
/// própria digita a sua — números fora do padrão são aceitos e só não
/// entram na conta do próximo.
/// </summary>
public static partial class NumeroContratoRegras
{
    public const int TamanhoMaximo = 30;

    /// <summary>Tira espaços das pontas e espaços repetidos; vazio vira nulo.</summary>
    public static string? Normalizar(string? numero)
    {
        if (string.IsNullOrWhiteSpace(numero))
            return null;

        return EspacosRepetidos().Replace(numero.Trim(), " ");
    }

    /// <summary>Próximo "ano/NNNN" depois do maior número desse ano no padrão (os fora do padrão não contam).</summary>
    public static string Proximo(int ano, IEnumerable<string?> existentes)
    {
        var maior = 0;
        foreach (var numero in existentes)
        {
            var sequencia = SequenciaDoAno(ano, numero);
            if (sequencia > maior)
                maior = sequencia;
        }

        return Formatar(ano, maior + 1);
    }

    public static string Formatar(int ano, int sequencia) =>
        $"{ano}/{sequencia.ToString("D4", CultureInfo.InvariantCulture)}";

    /// <summary>Sequência de um número "ano/NNNN" (aceita "2026/7", " 2026 / 0007 "); 0 se não for desse ano/padrão.</summary>
    public static int SequenciaDoAno(int ano, string? numero)
    {
        if (string.IsNullOrWhiteSpace(numero))
            return 0;

        var m = PadraoAnoSequencia().Match(numero);
        if (!m.Success || m.Groups[1].Value != ano.ToString(CultureInfo.InvariantCulture))
            return 0;

        return int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seq) ? seq : 0;
    }

    /// <summary>
    /// Como o contrato aparece em listas, e-mails e títulos:
    /// "Contrato 2026/0007 — Folha filial", "Contrato 2026/0007",
    /// ou só a descrição (contrato ainda sem número).
    /// </summary>
    public static string Rotulo(string? numero, string? descricao)
    {
        var temNumero = !string.IsNullOrWhiteSpace(numero);
        var temDescricao = !string.IsNullOrWhiteSpace(descricao);
        return (temNumero, temDescricao) switch
        {
            (true, true) => $"Contrato {numero!.Trim()} — {descricao!.Trim()}",
            (true, false) => $"Contrato {numero!.Trim()}",
            (false, true) => descricao!.Trim(),
            _ => "Contrato"
        };
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex EspacosRepetidos();

    [GeneratedRegex(@"^\s*(\d{4})\s*/\s*(\d{1,9})\s*$")]
    private static partial Regex PadraoAnoSequencia();
}
