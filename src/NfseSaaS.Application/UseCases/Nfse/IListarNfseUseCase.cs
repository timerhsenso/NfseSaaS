using NfseSaaS.Application.Common;

namespace NfseSaaS.Application.UseCases.Nfse;

public interface IListarNfseUseCase
{
    Task<PagedResult<NfseResponse>> ExecutarAsync(ListarNfseRequest request, CancellationToken cancellationToken);
}
