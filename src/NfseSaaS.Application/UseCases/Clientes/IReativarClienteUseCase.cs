namespace NfseSaaS.Application.UseCases.Clientes;

public interface IReativarClienteUseCase
{
    Task ExecutarAsync(Guid id, CancellationToken cancellationToken);
}
