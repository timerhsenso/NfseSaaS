namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>
/// Número oficial da NFS-e (nNFSe) — o número curto que o cliente conhece
/// ("44"), nunca o Id da nota ("NFS" + chave de 50 dígitos) nem o Id da DPS
/// ("DPS" + 42 dígitos).
///
/// A chave de acesso da NFS-e Nacional (50 dígitos) carrega o número dentro:
/// cLocEmi(7) + ambGer(1) + tpInsc(1) + inscrição federal(14) + nNFSe(13)
/// + AAMM(4) + código numérico(9) + DV(1). Por isso dá para recuperar o
/// número mesmo de nota sem XML guardado.
/// </summary>
public static class NumeroNfseOficial
{
    public const int TamanhoChave = 50;
    private const int InicioNumeroNaChave = 23;
    private const int TamanhoNumeroNaChave = 13;
    private const int TamanhoMaximoNumero = 15;

    /// <summary>
    /// Número da NFS-e contido na chave de acesso (aceita com ou sem o
    /// prefixo "NFS"). Null se o texto não for uma chave válida.
    /// </summary>
    public static string? DaChave(string? chaveOuId)
    {
        if (string.IsNullOrWhiteSpace(chaveOuId))
            return null;

        var chave = chaveOuId.Trim();
        if (chave.StartsWith("NFS", StringComparison.OrdinalIgnoreCase))
            chave = chave[3..];

        if (chave.Length != TamanhoChave || !chave.All(char.IsAsciiDigit))
            return null;

        return SemZerosAEsquerda(chave.Substring(InicioNumeroNaChave, TamanhoNumeroNaChave));
    }

    /// <summary>
    /// Deixa o valor no formato do número oficial: número curto fica como
    /// está (sem zeros à esquerda); Id da nota ("NFS…") vira o número que
    /// está na chave; Id da DPS ou qualquer outra coisa vira null.
    /// </summary>
    public static string? Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var texto = valor.Trim();
        if (texto.Length <= TamanhoMaximoNumero && texto.All(char.IsAsciiDigit))
            return SemZerosAEsquerda(texto);

        return texto.StartsWith("NFS", StringComparison.OrdinalIgnoreCase) ? DaChave(texto) : null;
    }

    /// <summary>True quando o valor gravado não é um número oficial "limpo" e precisa de correção.</summary>
    public static bool PrecisaCorrigir(string? valorGravado) =>
        valorGravado is not null && Normalizar(valorGravado) != valorGravado;

    private static string SemZerosAEsquerda(string numero)
    {
        var limpo = numero.TrimStart('0');
        return limpo.Length == 0 ? "0" : limpo;
    }
}
