namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>Caso de uso: cancelar uma NFS-e autorizada (evento e101101 na SEFIN Nacional).</summary>
public interface ICancelarNfseUseCase
{
    Task<CancelarNfseResult> ExecutarAsync(Guid nfseId, CancelarNfseRequest request, CancellationToken cancellationToken);
}
