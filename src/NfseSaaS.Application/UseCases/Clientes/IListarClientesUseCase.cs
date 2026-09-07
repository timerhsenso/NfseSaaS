using NfseSaaS.Application.Common;

namespace NfseSaaS.Application.UseCases.Clientes;

public interface IListarClientesUseCase
{
    Task<PagedResult<ClienteResponse>> ExecutarAsync(ListarClientesRequest request, CancellationToken cancellationToken);
}
