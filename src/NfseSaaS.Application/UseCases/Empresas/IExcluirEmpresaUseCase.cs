namespace NfseSaaS.Application.UseCases.Empresas;

/// <summary>Exclusão REAL (não confundir com IDesativarEmpresaUseCase, que é soft delete).</summary>
public interface IExcluirEmpresaUseCase
{
    Task ExecutarAsync(Guid id, CancellationToken cancellationToken);
}
