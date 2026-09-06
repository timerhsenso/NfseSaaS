namespace NfseSaaS.Nacional.Exceptions;

/// <summary>Erro de comunicação (HTTP/mTLS) com a SEFIN Nacional — falha de transporte, não rejeição de negócio.</summary>
public sealed class NfseApiException : Exception
{
    public NfseApiException(string message) : base(message) { }
    public NfseApiException(string message, Exception innerException) : base(message, innerException) { }
}
