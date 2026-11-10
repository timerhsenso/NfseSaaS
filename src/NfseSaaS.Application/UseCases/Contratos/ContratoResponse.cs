using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Contratos;

public sealed record ContratoResponse(
    Guid Id,
    Guid EmpresaId,
    Guid ClienteId,
    // Nome de EXIBIÇÃO do Cliente = Cliente.Apelido (não a razão social).
    string ClienteNome,
    string Descricao,
    IReadOnlyList<ContratoServicoResponse> Servicos,
    decimal ValorAtual,
    DateOnly DataInicioContrato,
    int PeriodicidadeReajusteMeses,
    IndiceReajusteContrato? IndiceReajuste,
    DateOnly? DataUltimoReajuste,
    int? DiasAlertaOverride,
    int DiasAlertaEfetivo,
    string? Observacao,
    DateOnly DataProximoReajuste,
    SituacaoContrato Situacao,
    StatusContrato Status,
    DateOnly? DataFim,
    TipoCobrancaContrato TipoCobranca,
    bool PermitirAlterarValorNaEmissao,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    bool? ReajusteAutomatico,
    bool AplicarIndiceNegativo,
    // Reajuste do aniversário aguardando aprovação (nulo se não há) — a
    // tela de Contratos mostra o selo e o Histórico de valores, a decisão.
    Guid? ReajustePendenteId,
    DateOnly? ReajustePendenteVigencia,
    // C2: local da prestação padrão das notas (nulo = município da Empresa).
    string? CodigoMunicipioPrestacao = null);
