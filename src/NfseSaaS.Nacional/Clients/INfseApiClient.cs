namespace NfseSaaS.Nacional.Clients;

/// <summary>Cliente HTTP para o Sistema Nacional da NFS-e (SEFIN Nacional), com mTLS por Empresa.</summary>
public interface INfseApiClient
{
    /// <summary>Envia a DPS (já assinada, compactada em GZip+Base64) via POST /nfse, usando o certificado mTLS da Empresa informada.</summary>
    Task<(int StatusCode, string Body)> EnviarDpsAsync(Guid empresaId, string dpsXmlGZipBase64, CancellationToken cancellationToken);

    /// <summary>Consulta uma NFS-e já emitida pela chave de acesso via GET /nfse/{chaveAcesso}, usando o certificado mTLS da Empresa informada.</summary>
    Task<(int StatusCode, string Body)> ConsultarPorChaveAsync(Guid empresaId, string chaveAcesso, CancellationToken cancellationToken);

    /// <summary>Envia um evento (já assinado, compactado em GZip+Base64) via POST /nfse/{chaveAcesso}/eventos, usando o certificado mTLS da Empresa informada.</summary>
    Task<(int StatusCode, string Body)> EnviarEventoAsync(Guid empresaId, string chaveAcesso, string eventoXmlGZipBase64, CancellationToken cancellationToken);
}
