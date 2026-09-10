namespace NfseSaaS.Nacional.Options;

/// <summary>
/// O ADN (Ambiente de Dados Nacional) é um serviço da NFS-e Nacional
/// DIFERENTE do SEFIN de emissão (NfseNacionalOptions) — host próprio,
/// mas usa o MESMO certificado mTLS por Empresa. Serve pra "puxar de
/// volta" documentos fiscais emitidos por QUALQUER canal (API, portal
/// web Emissor Nacional, outro sistema) em que o CNPJ da Empresa figure
/// como emitente, tomador ou intermediário — mecanismo oficial de
/// "Distribuição de DF-e por NSU".
///
/// Mesma dualidade de URL do NfseNacionalOptions: Homologação
/// (adn.producaorestrita.nfse.gov.br) e Produção (adn.nfse.gov.br) são
/// hosts diferentes de verdade.
/// </summary>
public sealed class AdnOptions
{
    public const string SectionName = "Adn";

    public string BaseUrlHomologacao { get; set; } = string.Empty;

    public string BaseUrlProducao { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Resolve a BaseUrl certa a partir do tpAmb ("1" = Produção, "2" = Homologação).</summary>
    public string ObterBaseUrl(string tpAmb) => tpAmb == "1" ? BaseUrlProducao : BaseUrlHomologacao;
}
