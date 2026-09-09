namespace NfseSaaS.Application.UseCases.Nfse;

public sealed record DanfsePdfResponse(byte[] Bytes, string ContentType, string NomeArquivo);
