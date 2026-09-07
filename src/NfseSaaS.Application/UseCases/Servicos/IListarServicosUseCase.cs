using NfseSaaS.Application.Common;

namespace NfseSaaS.Application.UseCases.Servicos;

public interface IListarServicosUseCase
{
    Task<PagedResult<ServicoResponse>> ExecutarAsync(ListarServicosRequest request, CancellationToken cancellationToken);
}
