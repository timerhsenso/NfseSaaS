namespace NfseSaaS.Nacional.Options;

/// <summary>
/// O ADN (Ambiente de Dados Nacional) é um serviço da NFS-e Nacional
/// DIFERENTE do SEFIN de emissão (NfseNacionalOptions) — host próprio
/// (adn.nfse.gov.br / adn.producaorestrita.nfse.gov.br), mas usa o MESMO
/// certificado mTLS por Empresa. Serve pra "puxar de volta" documentos
/// fiscais emitidos por QUALQUER canal (API, portal web Emissor Nacional,
/// outro sistema) em que o CNPJ da Empresa figure como emitente, tomador
/// ou intermediário — mecanismo oficial de "Distribuição de DF-e por NSU".
/// </summary>
public sealed class AdnOptions
{
    public const string SectionName = "Adn";

    /// <summary>Ex.: https://adn.producaorestrita.nfse.gov.br/contribuintes (homologação) ou https://adn.nfse.gov.br/contribuintes (produção).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 60;
}
