namespace NfseSaaS.Application.UseCases.Contratos;

public interface IAtualizarContratoUseCase
{
    Task ExecutarAsync(Guid id, AtualizarContratoRequest request, CancellationToken cancellationToken);
}
