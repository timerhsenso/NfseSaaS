namespace NfseSaaS.Application.UseCases.Contratos;

public interface IDesativarContratoUseCase
{
    Task ExecutarAsync(Guid id, CancellationToken cancellationToken);
}
