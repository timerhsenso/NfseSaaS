namespace NfseSaaS.Application.UseCases.Clientes;

public interface IAtualizarClienteUseCase
{
    Task ExecutarAsync(Guid id, AtualizarClienteRequest request, CancellationToken cancellationToken);
}
