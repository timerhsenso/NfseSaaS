namespace NfseSaaS.Application.UseCases.HistoricoValoresContrato;

/// <summary>Histórico de valores de um Contrato, do evento mais recente pro mais antigo.</summary>
public interface IListarHistoricoValoresContratoUseCase
{
    Task<IReadOnlyList<MovimentacaoValorContratoResponse>> ExecutarAsync(Guid contratoId, CancellationToken cancellationToken);
}
