namespace NfseSaaS.Nacional.Exceptions;

/// <summary>Falha ao carregar/validar o certificado digital (arquivo ausente, senha incorreta, sem chave privada, fora da validade).</summary>
public sealed class NfseCertificateException : Exception
{
    public NfseCertificateException(string message) : base(message) { }
    public NfseCertificateException(string message, Exception innerException) : base(message, innerException) { }
}
