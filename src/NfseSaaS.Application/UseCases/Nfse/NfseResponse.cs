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
    string? CodigoErro,
    string? MensagemErro,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
