namespace NfseSaaS.Application.UseCases.Contratos;

public sealed record ContratoServicoRequest(
    Guid ServicoId,
    decimal Quantidade,
    decimal ValorUnitario);
