namespace NfseSaaS.Application.UseCases.ReajustesContrato;

public sealed record RegistrarReajusteRequest(
    DateOnly DataReajuste,
    decimal ValorNovo,
    string? IndiceUsado,
    string? Observacao);
