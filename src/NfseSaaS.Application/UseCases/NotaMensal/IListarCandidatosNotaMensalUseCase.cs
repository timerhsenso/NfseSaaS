namespace NfseSaaS.Application.UseCases.NotaMensal;

public interface IListarCandidatosNotaMensalUseCase
{
    Task<IReadOnlyList<ContratoCandidatoNotaMensalResponse>> ExecutarAsync(Guid empresaId, DateOnly competencia, CancellationToken cancellationToken);
}
