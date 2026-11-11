namespace NfseSaaS.Application.UseCases.Contratos;

public sealed record ContratoServicoRequest(
    Guid ServicoId,
    decimal Quantidade,
    decimal ValorUnitario,
    // Valor muda a cada nota (Nota Mensal sugere o último emitido).
    bool ValorVariavel = false);
