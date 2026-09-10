namespace NfseSaaS.Application.UseCases.Consultas;

public interface IConsultarCnpjUseCase
{
    Task<ConsultaCnpjResponse> ExecutarAsync(string cnpj, CancellationToken cancellationToken);
}
