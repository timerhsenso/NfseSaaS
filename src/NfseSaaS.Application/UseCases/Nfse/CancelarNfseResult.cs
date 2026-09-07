namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>Resultado do caso de uso de cancelamento de NFS-e.</summary>
public sealed record CancelarNfseResult(
    Guid NfseId,
    bool Sucesso,
    string? CodigoErro,
    string? MensagemErro);
