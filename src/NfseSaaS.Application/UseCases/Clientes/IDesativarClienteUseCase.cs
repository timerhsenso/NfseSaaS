namespace NfseSaaS.Application.UseCases.Clientes;

public interface IDesativarClienteUseCase
{
    Task ExecutarAsync(Guid id, CancellationToken cancellationToken);
}
