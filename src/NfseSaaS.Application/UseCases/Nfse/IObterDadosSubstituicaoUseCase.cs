namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>
/// C7: o que a tela precisa para abrir a substituição de uma nota: os motivos
/// oficiais e os campos que o regime da empresa não deixa mudar.
/// </summary>
public interface IObterDadosSubstituicaoUseCase
{
    Task<DadosSubstituicaoResponse> ExecutarAsync(Guid nfseId, CancellationToken cancellationToken);
}

/// <param name="Impedimento">Nulo = pode substituir; senão, o motivo (mostrado na tela).</param>
public sealed record DadosSubstituicaoResponse(
    string? Impedimento,
    IReadOnlyList<MotivoSubstituicao> Motivos,
    IReadOnlyList<CampoTravadoSubstituicao> CamposTravados,
    string TextoCamposTravados);
