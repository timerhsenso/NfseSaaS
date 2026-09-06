namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>Caso de uso: cancelar uma NFS-e autorizada. Implementação prevista para a Fase 2.</summary>
public interface ICancelarNfseUseCase
{
    Task<bool> ExecutarAsync(Guid nfseId, string motivo, CancellationToken cancellationToken);
}
