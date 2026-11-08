namespace NfseSaaS.Application.UseCases.SincronizacaoSefin;

/// <summary>O que a tela diz quando a busca de notas na SEFIN (ADN) não dá certo.</summary>
public static class MensagensSincronizacao
{
    public const string NadaAlterado = " Nada foi alterado: a próxima busca recomeça do mesmo ponto.";

    public const string Indisponivel =
        "A SEFIN Nacional não respondeu à busca de notas agora (fora do ar ou lenta). Tente de novo em alguns minutos." + NadaAlterado;

    /// <param name="motivo">Erros informados pelo ADN; nulo quando ele recusa sem dizer o motivo.</param>
    public static string Recusa(string? motivo) =>
        string.IsNullOrWhiteSpace(motivo)
            ? "A SEFIN Nacional recusou a busca de notas desta empresa sem informar o motivo (acontece às vezes, principalmente em homologação). Tente de novo mais tarde; se continuar, confira em Empresas › Certificado se o certificado é do mesmo CNPJ da empresa." + NadaAlterado
            : $"A SEFIN Nacional recusou a busca de notas desta empresa: {motivo.Trim().TrimEnd('.')}." + NadaAlterado;
}
