namespace NfseSaaS.Application.UseCases.SincronizacaoSefin;

/// <summary>
/// Puxa, via Distribuição de DF-e (ADN), as NFS-e emitidas por QUALQUER
/// canal (portal web Emissor Nacional, outro sistema) em que o CNPJ da
/// Empresa figure como emitente — e que ainda não existem neste SaaS.
/// </summary>
public interface ISincronizarNotasDaSefinUseCase
{
    Task<SincronizacaoSefinResponse> ExecutarAsync(Guid empresaId, CancellationToken cancellationToken);
}
