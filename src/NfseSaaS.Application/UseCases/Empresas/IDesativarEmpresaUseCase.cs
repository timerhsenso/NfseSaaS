namespace NfseSaaS.Application.UseCases.Empresas;

public interface IDesativarEmpresaUseCase
{
    Task ExecutarAsync(Guid id, CancellationToken cancellationToken);
}
