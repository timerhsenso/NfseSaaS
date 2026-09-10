namespace NfseSaaS.Application.UseCases.DocumentosContrato;

public interface IExcluirDocumentoContratoUseCase
{
    Task ExecutarAsync(Guid documentoId, CancellationToken cancellationToken);
}
