namespace NfseSaaS.Application.UseCases.ReajustesContrato;

public interface IRegistrarReajusteUseCase
{
    Task<Guid> ExecutarAsync(Guid contratoId, RegistrarReajusteRequest request, CancellationToken cancellationToken);
}
