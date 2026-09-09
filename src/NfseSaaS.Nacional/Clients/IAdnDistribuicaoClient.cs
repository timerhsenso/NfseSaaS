using NfseSaaS.Nacional.Models;

namespace NfseSaaS.Nacional.Clients;

/// <summary>
/// Consulta o ADN (Ambiente de Dados Nacional) por NSU — distribuição de
/// DF-e, ver comentário completo em AdnOptions.
/// </summary>
public interface IAdnDistribuicaoClient
{
    /// <summary>Retorna todos os documentos disponíveis com NSU maior que o informado.</summary>
    Task<DfeLoteResponse> ConsultarPorNsuAsync(Guid empresaId, long ultimoNsuProcessado, CancellationToken cancellationToken);
}
