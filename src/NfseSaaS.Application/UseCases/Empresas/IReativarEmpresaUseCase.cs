namespace NfseSaaS.Application.UseCases.Empresas;

public interface IReativarEmpresaUseCase
{
    Task ExecutarAsync(Guid id, CancellationToken cancellationToken);
}
