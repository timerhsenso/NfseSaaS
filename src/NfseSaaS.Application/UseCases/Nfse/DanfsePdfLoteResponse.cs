namespace NfseSaaS.Application.UseCases.Nfse;

public sealed record DanfsePdfLoteResponse(
    byte[] ZipBytes,
    string NomeArquivoZip,
    IReadOnlyList<Guid> IdsIgnorados);
