namespace NfseSaaS.Application.UseCases.Empresas;

public interface IObterEmpresaPorIdUseCase
{
    Task<EmpresaResponse> ExecutarAsync(Guid id, CancellationToken cancellationToken);
}
