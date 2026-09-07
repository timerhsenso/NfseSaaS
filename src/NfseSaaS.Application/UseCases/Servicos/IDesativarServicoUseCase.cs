namespace NfseSaaS.Application.UseCases.Servicos;

public interface IDesativarServicoUseCase
{
    Task ExecutarAsync(Guid id, CancellationToken cancellationToken);
}
