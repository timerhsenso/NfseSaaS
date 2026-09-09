namespace NfseSaaS.Application.UseCases.Certificados;

/// <summary>
/// Testa se o certificado da Empresa é aceito num handshake mTLS real com
/// a SEFIN Nacional — não emite nada, só confirma que a conexão/certificado
/// funcionam antes do usuário tentar emitir uma nota de verdade.
/// </summary>
public interface ITestarConexaoCertificadoUseCase
{
    Task<TesteConexaoResponse> ExecutarAsync(Guid empresaId, CancellationToken cancellationToken);
}
