namespace NfseSaaS.Application.UseCases.Contratos;

/// <summary>Número proposto para o próximo contrato da empresa ("ano/NNNN"), mostrado ao abrir "Novo contrato".</summary>
public interface IObterProximoNumeroContratoUseCase
{
    Task<string> ExecutarAsync(Guid empresaId, CancellationToken cancellationToken);
}
