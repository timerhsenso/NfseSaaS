namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>
/// Obtém o DANFSe (representação gráfica em PDF) de uma Nfse já
/// autorizada, direto da SEFIN Nacional — não gera nada localmente.
/// </summary>
public interface IObterDanfsePdfUseCase
{
    Task<DanfsePdfResponse> ExecutarAsync(Guid nfseId, CancellationToken cancellationToken);
}
