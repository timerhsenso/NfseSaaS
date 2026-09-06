namespace NfseSaaS.Nacional.Clients;

/// <summary>Cliente HTTP para o Sistema Nacional da NFS-e (SEFIN Nacional).</summary>
public interface INfseApiClient
{
    /// <summary>Envia a DPS (já assinada, compactada em GZip+Base64) via POST /nfse. Retorna o corpo bruto (JSON) da resposta.</summary>
    Task<(int StatusCode, string Body)> EnviarDpsAsync(string dpsXmlGZipBase64, CancellationToken cancellationToken);

    /// <summary>Consulta uma NFS-e já emitida pela chave de acesso via GET /nfse/{chaveAcesso}.</summary>
    Task<(int StatusCode, string Body)> ConsultarPorChaveAsync(string chaveAcesso, CancellationToken cancellationToken);
}
