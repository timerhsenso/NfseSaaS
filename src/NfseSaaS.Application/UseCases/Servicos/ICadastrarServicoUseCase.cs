namespace NfseSaaS.Application.UseCases.Servicos;

/// <summary>Caso de uso: cadastrar um serviço prestado pela empresa. Implementação prevista para a Fase 2.</summary>
public interface ICadastrarServicoUseCase
{
    Task<Guid> ExecutarAsync(CadastrarServicoRequest request, CancellationToken cancellationToken);
}
