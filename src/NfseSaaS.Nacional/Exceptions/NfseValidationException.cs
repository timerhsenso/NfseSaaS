namespace NfseSaaS.Nacional.Exceptions;

/// <summary>DPS rejeitada pela SEFIN Nacional (ex.: E0008, E0010) ou reprovada em validação local antes do envio.</summary>
public sealed class NfseValidationException : Exception
{
    public IReadOnlyCollection<string> Codigos { get; }

    public NfseValidationException(string message, IReadOnlyCollection<string>? codigos = null)
        : base(message)
    {
        Codigos = codigos ?? Array.Empty<string>();
    }
}
