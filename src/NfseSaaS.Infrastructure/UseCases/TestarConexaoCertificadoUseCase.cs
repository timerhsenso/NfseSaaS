using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Certificados;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Nacional.Clients;
using NfseSaaS.Nacional.Exceptions;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Testa a conexão mTLS de verdade: chama GET /nfse/{chave} na SEFIN
/// Nacional com uma chave que não existe. NÃO importa o que a SEFIN
/// responde (400, 404, o que for) — qualquer resposta HTTP de volta já
/// prova que o handshake TLS com autenticação por certificado de cliente
/// foi aceito. Só uma falha na camada de comunicação (certificado
/// recusado, rede, timeout) indica problema real, e é isso que
/// NfseApiException/NfseCertificateException sinalizam.
/// </summary>
public sealed class TestarConexaoCertificadoUseCase : ITestarConexaoCertificadoUseCase
{
    // Chave de acesso tem formato fixo (44 dígitos) mas não precisa
    // corresponder a uma nota real — só precisa ser aceita pela rota da
    // API pra chegar até o ponto de precisar do handshake mTLS.
    private const string ChaveDeTeste = "00000000000000000000000000000000000000000000";

    private readonly AppDbContext _db;
    private readonly INfseApiClient _apiClient;

    public TestarConexaoCertificadoUseCase(AppDbContext db, INfseApiClient apiClient)
    {
        _db = db;
        _apiClient = apiClient;
    }

    public async Task<TesteConexaoResponse> ExecutarAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        // Ver comentário completo em EnviarCertificadoUseCase — mesma
        // checagem obrigatória de tenant via _db.Empresas.
        var empresaExiste = await _db.Empresas.AnyAsync(e => e.Id == empresaId, cancellationToken);
        if (!empresaExiste)
            throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");

        try
        {
            var (statusCode, _) = await _apiClient.ConsultarPorChaveAsync(empresaId, ChaveDeTeste, cancellationToken);

            return new TesteConexaoResponse(
                Sucesso: true,
                Mensagem: $"Conexão mTLS estabelecida com sucesso — a SEFIN Nacional respondeu (HTTP {statusCode}).");
        }
        catch (NfseCertificateException ex)
        {
            return new TesteConexaoResponse(false, ex.Message);
        }
        catch (NfseApiException ex)
        {
            return new TesteConexaoResponse(false, ex.Message);
        }
    }
}
