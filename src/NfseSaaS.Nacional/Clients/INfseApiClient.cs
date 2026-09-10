namespace NfseSaaS.Nacional.Clients;

/// <summary>Cliente HTTP para o Sistema Nacional da NFS-e (SEFIN Nacional), com mTLS por Empresa.</summary>
public interface INfseApiClient
{
    /// <summary>Envia a DPS (já assinada, compactada em GZip+Base64) via POST /nfse, usando o certificado mTLS da Empresa informada.</summary>
    /// <param name="tpAmb">"1" = Produção, "2" = Homologação — decide qual das duas URLs (NfseNacionalOptions) é chamada.</param>
    Task<(int StatusCode, string Body)> EnviarDpsAsync(Guid empresaId, string tpAmb, string dpsXmlGZipBase64, CancellationToken cancellationToken);

    /// <summary>Consulta uma NFS-e já emitida pela chave de acesso via GET /nfse/{chaveAcesso}, usando o certificado mTLS da Empresa informada.</summary>
    /// <param name="tpAmb">"1" = Produção, "2" = Homologação.</param>
    Task<(int StatusCode, string Body)> ConsultarPorChaveAsync(Guid empresaId, string tpAmb, string chaveAcesso, CancellationToken cancellationToken);

    /// <summary>
    /// Baixa a representação gráfica (DANFSe) em PDF via GET /danfse/{chaveAcesso}.
    /// ATENÇÃO: este endpoint no ambiente NACIONAL foi reportado como
    /// descontinuado (NT 008/2026, 03/08/2026) em favor de geração local
    /// do PDF a partir do XML — não confirmado se afeta o ambiente da
    /// SEFIN Nacional usado por este projeto. Se retornar 404/erro de
    /// forma consistente, é sinal de que a geração precisa passar a ser
    /// local (fora do escopo desta implementação).
    /// </summary>
    /// <param name="tpAmb">"1" = Produção, "2" = Homologação — deve ser o ambiente em que a nota foi emitida, não o ambiente atual da Empresa.</param>
    Task<(int StatusCode, byte[] Bytes, string? ContentType)> ObterDanfsePdfAsync(Guid empresaId, string tpAmb, string chaveAcesso, CancellationToken cancellationToken);

    /// <summary>Envia um evento (já assinado, compactado em GZip+Base64) via POST /nfse/{chaveAcesso}/eventos, usando o certificado mTLS da Empresa informada.</summary>
    /// <param name="tpAmb">"1" = Produção, "2" = Homologação — deve ser o ambiente em que a nota foi emitida, não o ambiente atual da Empresa.</param>
    Task<(int StatusCode, string Body)> EnviarEventoAsync(Guid empresaId, string tpAmb, string chaveAcesso, string eventoXmlGZipBase64, CancellationToken cancellationToken);
}
