namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>XML da NFS-e autorizada pela SEFIN, como foi guardado (o arquivo que o contador importa).</summary>
public sealed record XmlNfseResponse(byte[] Bytes, string NomeArquivo);

/// <summary>Download do XML de uma nota Autorizada ou Cancelada (mesmo nome do PDF, com .xml).</summary>
public interface IObterXmlNfseUseCase
{
    Task<XmlNfseResponse> ExecutarAsync(Guid nfseId, CancellationToken cancellationToken);
}
