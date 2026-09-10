namespace NfseSaaS.Application.UseCases.Contratos;

public interface IReativarContratoUseCase
{
    Task ExecutarAsync(Guid id, CancellationToken cancellationToken);
}
