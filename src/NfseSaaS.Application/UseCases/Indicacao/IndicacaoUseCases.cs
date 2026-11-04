using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace NfseSaaS.Application.UseCases.Indicacao;

/// <summary>
/// Regras do código de indicação e da origem do cadastro (endereço do tipo
/// micoempresa.com.br/?ref=K7M2QX9A ou ?origem=email-nota).
/// </summary>
public static partial class CodigoIndicacao
{
    /// <summary>Sem 0/O, 1/I/L, para não confundir quem digita o código.</summary>
    private const string Alfabeto = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Tamanho = 8;

    /// <summary>Origem gravada quando o cadastro veio por link de indicação sem outra origem.</summary>
    public const string OrigemIndicacao = "indicacao";

    public static string Gerar()
    {
        Span<char> codigo = stackalloc char[Tamanho];
        for (var i = 0; i < Tamanho; i++)
            codigo[i] = Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)];
        return new string(codigo);
    }

    /// <summary>Código no formato aceito (maiúsculas), ou null quando vazio ou inválido.</summary>
    public static string? Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var codigo = valor.Trim().ToUpperInvariant();
        return codigo.Length == Tamanho && codigo.All(c => Alfabeto.Contains(c)) ? codigo : null;
    }

    /// <summary>Origem segura para gravar (minúsculas, números e hífen, até 40), ou null.</summary>
    public static string? NormalizarOrigem(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var origem = valor.Trim().ToLowerInvariant();
        return RegexOrigem().IsMatch(origem) ? origem : null;
    }

    [GeneratedRegex("^[a-z0-9-]{2,40}$")]
    private static partial Regex RegexOrigem();
}

/// <summary>Uma conta que se cadastrou pelo link de indicação. Sem nome: só o necessário para acompanhar.</summary>
public sealed record ContaIndicada(DateTimeOffset CadastradaEm, bool JaEmitiuNota, bool Ativa);

/// <summary>Dados da tela "Indique e ganhe".</summary>
public sealed record IndicacaoResponse(string Codigo, IReadOnlyList<ContaIndicada> Indicadas);

/// <summary>Código de indicação da conta atual (cria na primeira vez) e as contas que vieram por ele.</summary>
public interface IObterIndicacaoUseCase
{
    Task<IndicacaoResponse> ExecutarAsync(CancellationToken cancellationToken);
}
