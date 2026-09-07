namespace NfseSaaS.Application.UseCases.Nfse;

public interface IObterNfsePorIdUseCase
{
    Task<NfseResponse> ExecutarAsync(Guid id, CancellationToken cancellationToken);
}
