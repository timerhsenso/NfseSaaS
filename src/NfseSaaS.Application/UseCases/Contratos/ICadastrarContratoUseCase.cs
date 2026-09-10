namespace NfseSaaS.Application.UseCases.Contratos;

public interface ICadastrarContratoUseCase
{
    Task<Guid> ExecutarAsync(CadastrarContratoRequest request, CancellationToken cancellationToken);
}
