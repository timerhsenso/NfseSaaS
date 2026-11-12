using NfseSaaS.Domain.Enums;
using NfseSaaS.Domain.Snapshots;

namespace NfseSaaS.Application.UseCases.HistoricoValoresContrato;

/// <summary>
/// Um evento do Histórico de valores do Contrato, como a tela mostra.
/// PodeDesfazer/BloqueioDesfazer: só o último reajuste aplicado tem o botão
/// "Desfazer" (ou o motivo de não poder, para a dica da tela).
/// </summary>
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
    DateTimeOffset CreatedAt,
    DateTimeOffset? DesfeitoEm = null,
    string? DesfeitoPor = null,
    string? MotivoDesfazer = null,
    bool PodeDesfazer = false,
    string? BloqueioDesfazer = null);
