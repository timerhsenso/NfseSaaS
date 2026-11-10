using System.Globalization;
using System.Text;

namespace NfseSaaS.Application.UseCases.Municipios;

/// <summary>
/// Municípios em tabela local (C9): busca pelo nome nas telas de Cliente e
/// de Empresa e conferência do código IBGE ao salvar (o cMun errado é
/// rejeitado pela SEFIN — E0238 no tomador). Sem depender do IBGE no ar.
/// </summary>
public static class RegrasMunicipio
{
    public const int LimiteBusca = 20;

    /// <summary>"São Paulo" → "sao paulo"; "D'Oeste" → "d'oeste". Espaços repetidos viram um.</summary>
    public static string NormalizarBusca(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        var semAcento = new StringBuilder(texto.Length);
        foreach (var c in texto.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                semAcento.Append(c);
        }

        var minusculo = semAcento.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        return string.Join(' ', minusculo.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>"salvador ba", "salvador/ba", "salvador - ba" → ("salvador", "BA").</summary>
    public static (string Termo, string? Uf) SepararUf(string? busca)
    {
        var texto = (busca ?? string.Empty).Replace('/', ' ').Replace('-', ' ').Replace(',', ' ').Trim();
        var partes = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length >= 2 && partes[^1].Length == 2 && partes[^1].All(char.IsLetter))
            return (NormalizarBusca(string.Join(' ', partes[..^1])), partes[^1].ToUpperInvariant());
        return (NormalizarBusca(texto), null);
    }

    public static string Exibicao(string nome, string uf) => $"{nome} — {uf}";

    public static string MensagemNaoExiste(string codigo) =>
        $"O código de município {codigo} não existe na tabela do IBGE. Busque o município pelo nome.";
}

public sealed record MunicipioResponse(string Codigo, string Nome, string Uf);

public interface IMunicipiosUseCase
{
    /// <summary>Até <paramref name="limite"/> municípios cujo nome começa (primeiro) ou contém o termo; aceita "nome UF".</summary>
    Task<IReadOnlyList<MunicipioResponse>> BuscarAsync(string? busca, string? uf, int limite, CancellationToken cancellationToken);

    Task<MunicipioResponse?> ObterAsync(string codigo, CancellationToken cancellationToken);
}
