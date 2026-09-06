namespace NfseSaaS.Application.UseCases.Clientes;

/// <summary>Caso de uso: cadastrar um cliente (tomador de serviço). Implementação prevista para a Fase 2.</summary>
public interface ICadastrarClienteUseCase
{
    Task<Guid> ExecutarAsync(CadastrarClienteRequest request, CancellationToken cancellationToken);
}
