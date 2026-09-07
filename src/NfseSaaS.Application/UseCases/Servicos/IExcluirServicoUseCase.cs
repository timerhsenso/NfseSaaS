namespace NfseSaaS.Application.UseCases.Servicos;

/// <summary>Exclusão REAL (não confundir com IDesativarServicoUseCase, que é soft delete).</summary>
public interface IExcluirServicoUseCase
{
    Task ExecutarAsync(Guid id, CancellationToken cancellationToken);
}
