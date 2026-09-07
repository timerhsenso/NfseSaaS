namespace NfseSaaS.Application.UseCases.Servicos;

public interface IAtualizarServicoUseCase
{
    Task ExecutarAsync(Guid id, AtualizarServicoRequest request, CancellationToken cancellationToken);
}
