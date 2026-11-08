namespace NfseSaaS.Nacional.Exceptions;

/// <summary>
/// O ADN respondeu, mas recusou a consulta de DF-e (HTTP 4xx/5xx com corpo).
/// Diferente de <see cref="NfseApiException"/> (falha de transporte): aqui
/// houve resposta, e o motivo dela (quando o ADN informa) vai para a tela.
/// </summary>
public sealed class AdnConsultaRecusadaException : Exception
{
    public AdnConsultaRecusadaException(int statusHttp, string? motivo, string corpo)
        : base($"ADN recusou a consulta (HTTP {statusHttp}): {motivo ?? "sem motivo informado"}. Corpo: {corpo}")
    {
        StatusHttp = statusHttp;
        Motivo = motivo;
        Corpo = corpo;
    }

    public int StatusHttp { get; }

    /// <summary>Erros informados pelo ADN, já em texto ("E2243 - descrição"); nulo quando ele não diz.</summary>
    public string? Motivo { get; }

    /// <summary>Corpo cru da resposta (para o log, não para a tela).</summary>
    public string Corpo { get; }
}
