namespace NfseSaaS.Application.UseCases.Empresas;

/// <summary>Caso de uso: cadastrar uma Empresa emissora de NFS-e dentro do Tenant atual.</summary>
public interface ICadastrarEmpresaUseCase
{
    Task<Guid> ExecutarAsync(CadastrarEmpresaRequest request, CancellationToken cancellationToken);
}
