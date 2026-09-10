namespace NfseSaaS.Application.UseCases.DocumentosContrato;

public interface IObterDocumentoContratoParaDownloadUseCase
{
    Task<DocumentoContratoParaDownloadResponse> ExecutarAsync(Guid documentoId, CancellationToken cancellationToken);
}
