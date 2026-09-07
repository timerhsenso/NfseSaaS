namespace NfseSaaS.Application.UseCases.Servicos;

public interface IObterServicoPorIdUseCase
{
    Task<ServicoResponse> ExecutarAsync(Guid id, CancellationToken cancellationToken);
}
