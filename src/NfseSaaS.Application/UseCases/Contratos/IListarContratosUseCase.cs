using NfseSaaS.Application.Common;

namespace NfseSaaS.Application.UseCases.Contratos;

public interface IListarContratosUseCase
{
    Task<PagedResult<ContratoResponse>> ExecutarAsync(ListarContratosRequest request, CancellationToken cancellationToken);
}
