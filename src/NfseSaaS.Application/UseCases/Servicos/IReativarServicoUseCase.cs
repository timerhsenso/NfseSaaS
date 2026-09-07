namespace NfseSaaS.Application.UseCases.Servicos;

public interface IReativarServicoUseCase
{
    Task ExecutarAsync(Guid id, CancellationToken cancellationToken);
}
