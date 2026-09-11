namespace NfseSaaS.Application.UseCases.NotaMensal;

public sealed record EmitirNotaMensalLoteRequest(
    Guid EmpresaId,
    DateOnly Competencia,
    IReadOnlyList<EmitirNotaMensalItemRequest> Itens);
