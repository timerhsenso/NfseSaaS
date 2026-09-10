namespace NfseSaaS.Application.UseCases.DocumentosContrato;

public interface IListarDocumentosContratoUseCase
{
    Task<IReadOnlyList<ContratoDocumentoResponse>> ExecutarAsync(Guid contratoId, CancellationToken cancellationToken);
}
