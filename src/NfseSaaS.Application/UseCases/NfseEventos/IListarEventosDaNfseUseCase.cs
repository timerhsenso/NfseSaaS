namespace NfseSaaS.Application.UseCases.NfseEventos;

/// <summary>
/// Histórico completo de uma Nfse, em ordem cronológica. Sem paginação
/// deliberadamente: o volume de eventos por Nfse é pequeno (um punhado de
/// transições ao longo do ciclo de vida), diferente de AuditLog (que
/// cresce por todo o sistema e por isso é paginado).
/// </summary>
public interface IListarEventosDaNfseUseCase
{
    Task<IReadOnlyList<NfseEventoResponse>> ExecutarAsync(Guid nfseId, CancellationToken cancellationToken);
}
