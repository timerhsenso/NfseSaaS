using NfseSaaS.Domain.Snapshots;

namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>
/// Endpoint dedicado pro snapshot fiscal — mesmo raciocínio já documentado
/// em NfseResponse pro XML: payload secundário, não carregado em toda
/// listagem/detalhe, só quando explicitamente pedido (suporte, auditoria).
/// Retorna null se a Nfse foi emitida antes deste campo existir (não
/// falha — snapshot é enriquecimento, não é dado obrigatório pra notas
/// antigas).
/// </summary>
public interface IObterSnapshotFiscalDaNfseUseCase
{
    Task<NfseSnapshotFiscal?> ExecutarAsync(Guid nfseId, CancellationToken cancellationToken);
}
