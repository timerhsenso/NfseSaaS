using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.ReajustesContrato;

public sealed record RegistrarReajusteRequest(
    DateOnly DataReajuste,
    decimal ValorNovo,
    IndiceReajusteContrato? IndiceUsado,
    string? Observacao);
