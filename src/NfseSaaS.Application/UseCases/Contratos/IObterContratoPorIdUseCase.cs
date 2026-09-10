namespace NfseSaaS.Application.UseCases.Contratos;

public interface IObterContratoPorIdUseCase
{
    Task<ContratoResponse> ExecutarAsync(Guid id, CancellationToken cancellationToken);
}
