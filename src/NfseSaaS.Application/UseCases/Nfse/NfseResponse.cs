using NfseSaaS.Application.Rejeicoes;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>
/// Não inclui XmlDps/XmlNfse (payload pesado, sem uso em listagem/tela).
/// Se algum dia for necessário expor o XML, criar endpoint dedicado
/// (ex.: GET /api/nfse/{id}/xml) em vez de carregar em toda listagem.
/// </summary>
/// <summary>C6: descontos e retenções federais de uma nota (R$; nulo = não houve).</summary>
public sealed record DescontosRetencoesNfse(
    decimal? DescontoIncondicionado,
    decimal? DescontoCondicionado,
    decimal? Irrf,
    decimal? Pis,
    decimal? Cofins,
    decimal? Csll,
    decimal? Inss)
{
    public decimal TotalFederal => (Irrf ?? 0) + (Pis ?? 0) + (Cofins ?? 0) + (Csll ?? 0) + (Inss ?? 0);
}

public sealed record NfseResponse(
    Guid Id,
    Guid EmpresaId,
    Guid ClienteId,
    Guid? ContratoId,
    int NumeroDps,
    string SerieDps,
    string? NumeroNfse,
    string? ChaveAcesso,
    DateOnly DataCompetencia,
    DateTimeOffset? DataEmissao,
    decimal ValorServico,
    decimal? ValorLiquido,
    string DescricaoServico,
    NfseStatus Status,
    // Ambiente em que ESTA nota foi emitida (congelado, ver
    // Nfse.TipoAmbiente) — não confundir com o ambiente atual da
    // Empresa, que pode já ter mudado.
    TipoAmbiente TipoAmbiente,
    string? CodigoErro,
    string? MensagemErro,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    Guid? ServicoId,
    // Reenvio de nota rejeitada: de qual tentativa esta veio, e (na
    // rejeitada) qual tentativa a substituiu. PodeReenviar já resolve a
    // regra inteira no backend (ver Nfse.RejeicaoPermiteReenvio) — a
    // tela só decide se mostra o botão.
    Guid? ReenvioDeNfseId,
    int? ReenvioDeNumeroDps,
    Guid? ReenviadaComoNfseId,
    int? ReenviadaComoNumeroDps,
    bool PodeReenviar,
    // Tributação desta nota (colunas congeladas na emissão) — para o
    // detalhe mostrar o que foi de fato para a SEFIN.
    string CodigoTributacaoNacional,
    string CodigoNbs,
    string TpRetIssqn,
    decimal? AliquotaIss,
    // C2: cLocPrestacao enviado (nulo nas notas antigas = município da Empresa).
    string? CodigoMunicipioPrestacao = null,
    // C7: qual nota esta substitui e (na original) por qual DPS foi substituída.
    Guid? SubstituiNfseId = null,
    int? SubstituiNumeroDps = null,
    int? SubstituidaPorNumeroDps = null,
    string? MotivoSubstituicao = null,
    string? DescricaoMotivoSubstituicao = null,
    // Valor de cada serviço do Contrato (só no detalhe; nulo na listagem).
    IReadOnlyList<ItemComposicaoNfse>? Composicao = null,
    // Último e-mail da nota ao cliente (envelope da lista): quando e como ficou. Nulo = nunca enviada.
    DateTimeOffset? EmailUltimoEnvioEm = null,
    StatusEmailLog? EmailUltimoStatus = null,
    // C6: descontos e retenções federais gravados na emissão (nulo = nenhum).
    DescontosRetencoesNfse? DescontosRetencoes = null,
    // CR1: primeira confirmação de recebimento (envelope verde) e quantas houve. Só na listagem.
    DateTimeOffset? RecebimentoConfirmadoEm = null,
    string? RecebimentoConfirmadoPor = null,
    int RecebimentoConfirmacoes = 0)
{
    /// <summary>tpRetISSQN "2" = ISS retido pelo tomador.</summary>
    public bool IssRetido => TpRetIssqn == "2";

    /// <summary>C7: só nota autorizada (com chave) e ainda não substituída pode ganhar substituta.</summary>
    public bool PodeSubstituir => Status == NfseStatus.Autorizada && !string.IsNullOrEmpty(ChaveAcesso) && SubstituidaPorNumeroDps is null;

    /// <summary>Rejeição da SEFIN explicada (onde corrigir e o que fazer) — null quando não há código de erro da SEFIN. Ver CatalogoRejeicoesSefin.</summary>
    public RejeicaoSefinTraduzida? Rejeicao => CatalogoRejeicoesSefin.Traduzir(CodigoErro, MensagemErro);
}
