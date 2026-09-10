namespace NfseSaaS.Application.UseCases.HistoricoContrato;

public interface IObterHistoricoContratoUseCase
{
    Task<IReadOnlyList<HistoricoContratoItemResponse>> ExecutarAsync(Guid contratoId, CancellationToken cancellationToken);
}
