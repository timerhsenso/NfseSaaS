namespace NfseSaaS.Application.UseCases.ReajustesContrato;

public interface IListarReajustesUseCase
{
    Task<IReadOnlyList<ReajusteContratoResponse>> ExecutarAsync(Guid contratoId, CancellationToken cancellationToken);
}
