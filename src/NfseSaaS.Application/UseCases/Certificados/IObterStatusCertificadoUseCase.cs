namespace NfseSaaS.Application.UseCases.Certificados;

public interface IObterStatusCertificadoUseCase
{
    Task<CertificadoStatusResponse> ExecutarAsync(Guid empresaId, CancellationToken cancellationToken);
}
