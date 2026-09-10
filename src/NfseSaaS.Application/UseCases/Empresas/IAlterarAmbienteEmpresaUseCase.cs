using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Empresas;

public interface IAlterarAmbienteEmpresaUseCase
{
    Task ExecutarAsync(Guid id, TipoAmbiente novoAmbiente, CancellationToken cancellationToken);
}
