namespace NfseSaaS.Application.UseCases.Contratos;

/// <summary>Exclusão REAL (não confundir com IDesativarContratoUseCase, que é soft delete).</summary>
public interface IExcluirContratoUseCase
{
    Task ExecutarAsync(Guid id, CancellationToken cancellationToken);
}
