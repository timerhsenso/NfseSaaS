namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>Caso de uso: consultar o status/dados de uma NFS-e já emitida. Implementação prevista para a Fase 2.</summary>
public interface IConsultarNfseUseCase
{
    Task<EmitirNfseResult?> ExecutarAsync(Guid nfseId, CancellationToken cancellationToken);
}
