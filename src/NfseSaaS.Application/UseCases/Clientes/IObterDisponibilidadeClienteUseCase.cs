namespace NfseSaaS.Application.UseCases.Clientes;

/// <summary>
/// O que a tela de Clientes precisa para o campo "Disponível para" (C10):
/// se ele aparece (conta com mais de uma empresa) e o padrão para cliente novo.
/// </summary>
public interface IObterDisponibilidadeClienteUseCase
{
    Task<DisponibilidadeClienteResponse> ExecutarAsync(CancellationToken cancellationToken);
}

public sealed record DisponibilidadeClienteResponse(bool MostrarCampo, bool PadraoCompartilhado, int TotalEmpresas);
