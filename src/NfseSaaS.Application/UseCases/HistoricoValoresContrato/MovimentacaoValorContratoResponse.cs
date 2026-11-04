using NfseSaaS.Domain.Enums;
using NfseSaaS.Domain.Snapshots;

namespace NfseSaaS.Application.UseCases.HistoricoValoresContrato;

/// <summary>Um evento do Histórico de valores do Contrato, como a tela mostra.</summary>
public sealed record MovimentacaoValorContratoResponse(
    Guid Id,
    Guid ContratoId,
    TipoMovimentacaoValor Tipo,
    StatusMovimentacaoValor Status,
    DateOnly DataVigencia,
    decimal? ValorAnterior,
    decimal ValorNovo,
    decimal? PercentualCalculado,
    decimal? PercentualAplicado,
    IndiceReajusteContrato? Indice,
    DateOnly? CompetenciaIndiceInicio,
    DateOnly? CompetenciaIndiceFim,
    string? Motivo,
    IReadOnlyList<ComposicaoContratoLinha> Composicao,
    bool Automatico,
    DateTimeOffset CreatedAt);
