using NfseSaaS.Application.Common;

namespace NfseSaaS.Application.UseCases.Empresas;

public interface IListarEmpresasUseCase
{
    Task<PagedResult<EmpresaResponse>> ExecutarAsync(ListarEmpresasRequest request, CancellationToken cancellationToken);
}
