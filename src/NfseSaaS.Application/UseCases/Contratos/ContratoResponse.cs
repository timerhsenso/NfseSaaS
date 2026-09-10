using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Contratos;

public sealed record ContratoResponse(
    Guid Id,
    Guid EmpresaId,
    Guid ClienteId,
    string ClienteNome,
    Guid ServicoId,
    string ServicoDescricao,
    string Descricao,
    decimal ValorAtual,
    DateOnly DataInicioContrato,
    int PeriodicidadeReajusteMeses,
    string? IndiceReajuste,
    DateOnly? DataUltimoReajuste,
    int? DiasAlertaOverride,
    int DiasAlertaEfetivo,
    DateOnly DataProximoReajuste,
    SituacaoContrato Situacao,
    bool Ativo,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
