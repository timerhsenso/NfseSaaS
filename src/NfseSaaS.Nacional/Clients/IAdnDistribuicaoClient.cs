using NfseSaaS.Nacional.Models;

namespace NfseSaaS.Nacional.Clients;

/// <summary>
/// Consulta o ADN (Ambiente de Dados Nacional) por NSU — distribuição de
/// DF-e, ver comentário completo em AdnOptions.
/// </summary>
public interface IAdnDistribuicaoClient
{
    /// <summary>Retorna todos os documentos disponíveis com NSU maior que o informado.</summary>
    /// <param name="tpAmb">"1" = Produção, "2" = Homologação — decide qual das duas URLs (AdnOptions) é chamada.</param>
    Task<DfeLoteResponse> ConsultarPorNsuAsync(Guid empresaId, string tpAmb, long ultimoNsuProcessado, CancellationToken cancellationToken);
}
