namespace NfseSaaS.Application.UseCases.DocumentosContrato;

/// <summary>
/// Recebe os bytes já lidos do arquivo (o Controller lê o IFormFile e
/// repassa aqui — mesmo padrão já usado em EnviarCertificadoUseCase,
/// Application não conhece tipos do ASP.NET Core).
/// </summary>
public interface IUploadDocumentoContratoUseCase
{
    Task<ContratoDocumentoResponse> ExecutarAsync(Guid contratoId, string nomeOriginal, byte[] conteudo, CancellationToken cancellationToken);
}
