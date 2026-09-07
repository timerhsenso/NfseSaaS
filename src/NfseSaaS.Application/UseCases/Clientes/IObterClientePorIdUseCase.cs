namespace NfseSaaS.Application.UseCases.Clientes;

public interface IObterClientePorIdUseCase
{
    Task<ClienteResponse> ExecutarAsync(Guid id, CancellationToken cancellationToken);
}
