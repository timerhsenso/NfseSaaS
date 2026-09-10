namespace NfseSaaS.Application.UseCases.Contratos;

public sealed record ContratoServicoResponse(
    Guid Id,
    Guid ServicoId,
    string ServicoDescricao,
    decimal Quantidade,
    decimal ValorUnitario,
    decimal ValorTotal);
