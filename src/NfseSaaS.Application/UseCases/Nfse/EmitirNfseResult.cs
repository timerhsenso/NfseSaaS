namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>Resultado do caso de uso de emissão de NFS-e (implementação na Fase 2).</summary>
public sealed record EmitirNfseResult(
    Guid NfseId,
    bool Sucesso,
    string? NumeroNfse,
    string? ChaveAcesso,
    string? CodigoErro,
    string? MensagemErro);
