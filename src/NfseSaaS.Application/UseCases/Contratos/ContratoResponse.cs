using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Contratos;

public sealed record ContratoResponse(
    Guid Id,
    Guid EmpresaId,
    Guid ClienteId,
    string ClienteNome,
    string Descricao,
    IReadOnlyList<ContratoServicoResponse> Servicos,
    decimal ValorAtual,
    DateOnly DataInicioContrato,
    int PeriodicidadeReajusteMeses,
    string? IndiceReajuste,
    DateOnly? DataUltimoReajuste,
    int? DiasAlertaOverride,
    int DiasAlertaEfetivo,
    DateOnly DataProximoReajuste,
    SituacaoContrato Situacao,
    StatusContrato Status,
    DateOnly? DataFim,
    TipoCobrancaContrato TipoCobranca,
    bool PermitirAlterarValorNaEmissao,
    bool Ativo,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
