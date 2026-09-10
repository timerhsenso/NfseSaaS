using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.ReajustesContrato;

public sealed record ReajusteContratoResponse(
    Guid Id,
    Guid ContratoId,
    DateOnly DataReajuste,
    decimal ValorAnterior,
    decimal ValorNovo,
    decimal? PercentualAplicado,
    IndiceReajusteContrato? IndiceUsado,
    string? Observacao,
    DateTimeOffset CreatedAt);
