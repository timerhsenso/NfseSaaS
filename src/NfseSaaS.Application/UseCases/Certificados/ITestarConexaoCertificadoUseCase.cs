using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Certificados;

/// <summary>
/// Testa se o certificado da Empresa é aceito num handshake mTLS real com
/// a SEFIN Nacional — não emite nada, só confirma que a conexão/certificado
/// funcionam antes do usuário tentar emitir uma nota de verdade.
/// </summary>
public interface ITestarConexaoCertificadoUseCase
{
    /// <summary>Testa no ambiente atual da Empresa (botão Testar conexão).</summary>
    Task<TesteConexaoResponse> ExecutarAsync(Guid empresaId, CancellationToken cancellationToken);

    /// <summary>Testa num ambiente escolhido (ex.: produção, antes de a Empresa passar para lá).</summary>
    Task<TesteConexaoResponse> ExecutarAsync(Guid empresaId, TipoAmbiente ambiente, CancellationToken cancellationToken);
}
