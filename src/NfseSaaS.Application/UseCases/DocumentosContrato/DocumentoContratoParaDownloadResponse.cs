namespace NfseSaaS.Application.UseCases.DocumentosContrato;

public sealed record DocumentoContratoParaDownloadResponse(
    string NomeOriginal,
    string ContentType,
    byte[] Conteudo);
