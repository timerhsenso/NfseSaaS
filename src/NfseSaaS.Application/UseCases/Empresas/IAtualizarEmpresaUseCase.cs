namespace NfseSaaS.Application.UseCases.Empresas;

public interface IAtualizarEmpresaUseCase
{
    Task ExecutarAsync(Guid id, AtualizarEmpresaRequest request, CancellationToken cancellationToken);
}
