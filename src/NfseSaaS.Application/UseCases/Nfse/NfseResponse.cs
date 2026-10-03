using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>
/// Não inclui XmlDps/XmlNfse (payload pesado, sem uso em listagem/tela).
/// Se algum dia for necessário expor o XML, criar endpoint dedicado
/// (ex.: GET /api/nfse/{id}/xml) em vez de carregar em toda listagem.
/// </summary>
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
    bool PodeReenviar);
