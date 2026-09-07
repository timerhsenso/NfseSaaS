namespace NfseSaaS.Application.UseCases.Clientes;

/// <summary>Exclusão REAL (não confundir com IDesativarClienteUseCase, que é soft delete).</summary>
public interface IExcluirClienteUseCase
{
    Task ExecutarAsync(Guid id, CancellationToken cancellationToken);
}
