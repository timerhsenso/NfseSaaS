using NfseSaaS.Application.Rejeicoes;

namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>Resultado do caso de uso de emissão de NFS-e (implementação na Fase 2).</summary>
public sealed record EmitirNfseResult(
    Guid NfseId,
    bool Sucesso,
    string? NumeroNfse,
    string? ChaveAcesso,
    string? CodigoErro,
    string? MensagemErro)
{
    /// <summary>Rejeição da SEFIN explicada (onde corrigir e o que fazer) — null quando não há código de erro da SEFIN. Ver CatalogoRejeicoesSefin.</summary>
    public RejeicaoSefinTraduzida? Rejeicao => CatalogoRejeicoesSefin.Traduzir(CodigoErro, MensagemErro);
}
