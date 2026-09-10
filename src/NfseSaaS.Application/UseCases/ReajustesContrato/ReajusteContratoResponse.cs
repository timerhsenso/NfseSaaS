namespace NfseSaaS.Application.UseCases.ReajustesContrato;

public sealed record ReajusteContratoResponse(
    Guid Id,
    Guid ContratoId,
    DateOnly DataReajuste,
    decimal ValorAnterior,
    decimal ValorNovo,
    decimal? PercentualAplicado,
    string? IndiceUsado,
    string? Observacao,
    DateTimeOffset CreatedAt);
