namespace NfseSaaS.Application.UseCases.Certificados;

/// <summary>
/// Recebe os BYTES do .pfx já lidos (não um IFormFile — Application não
/// conhece ASP.NET Core; a leitura do arquivo de upload é feita no
/// Controller, que só passa os bytes crus pra este caso de uso).
/// </summary>
public interface IEnviarCertificadoUseCase
{
    Task<CertificadoStatusResponse> ExecutarAsync(Guid empresaId, byte[] pfxBytes, string senha, CancellationToken cancellationToken);
}
