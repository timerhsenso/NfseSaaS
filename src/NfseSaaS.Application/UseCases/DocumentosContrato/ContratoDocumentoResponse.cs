namespace NfseSaaS.Application.UseCases.DocumentosContrato;

public sealed record ContratoDocumentoResponse(
    Guid Id,
    Guid ContratoId,
    string NomeOriginal,
    string Extensao,
    long TamanhoBytes,
    DateTimeOffset CreatedAt);
