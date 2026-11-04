using NfseSaaS.Application.Rejeicoes;

namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>Resultado do caso de uso de cancelamento de NFS-e.</summary>
public sealed record CancelarNfseResult(
    Guid NfseId,
    bool Sucesso,
    string? CodigoErro,
    string? MensagemErro)
{
    /// <summary>Rejeição da SEFIN explicada (onde corrigir e o que fazer) — null quando não há código de erro da SEFIN. Ver CatalogoRejeicoesSefin.</summary>
    public RejeicaoSefinTraduzida? Rejeicao => CatalogoRejeicoesSefin.Traduzir(CodigoErro, MensagemErro);
}
