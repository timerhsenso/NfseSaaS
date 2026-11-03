namespace NfseSaaS.Nacional.Options;

/// <summary>
/// Configuração fortemente tipada da integração com a SEFIN Nacional,
/// vinculada via IOptions&lt;NfseNacionalOptions&gt; à seção "NfseNacional"
/// do appsettings.
///
/// DUAS URLs, não uma: Homologação e Produção são endpoints REAIS e
/// diferentes da SEFIN Nacional (não é só uma flag tpAmb no mesmo host).
/// Qual delas é usada em cada chamada é decidido POR CHAMADA, a partir
/// do tpAmb resolvido pelo caller (empresa ou nota, conforme o caso) —
/// ver ObterBaseUrl. Nunca existe uma "URL do sistema" única.
/// </summary>
public sealed class NfseNacionalOptions
{
    public const string SectionName = "NfseNacional";

    public string BaseUrlHomologacao { get; set; } = string.Empty;

    public string BaseUrlProducao { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Leiaute da DPS exigido hoje pela Produção Restrita. Default Nt009:
    /// desde 29/10/2026 a SEFIN de homologação rejeita o leiaute antigo
    /// com E1235 (falta finNFSe antes de cLocEmi).
    /// </summary>
    public LeiauteDps LeiauteDpsHomologacao { get; set; } = LeiauteDps.Nt009;

    /// <summary>
    /// Leiaute da DPS exigido hoje pela Produção. Default Nt004 até a NT 009
    /// entrar em Produção (previsão oficial: 01/12/2026). Nesse dia basta
    /// configurar <c>NfseNacional__LeiauteDpsProducao=Nt009</c> (variável de
    /// ambiente) ou a chave equivalente no appsettings.Production.json —
    /// sem deploy. Trocar ANTES da data faz a Produção rejeitar toda DPS.
    /// </summary>
    public LeiauteDps LeiauteDpsProducao { get; set; } = LeiauteDps.Nt004;

    /// <summary>Resolve a BaseUrl certa a partir do tpAmb ("1" = Produção, "2" = Homologação).</summary>
    public string ObterBaseUrl(string tpAmb) => tpAmb == "1" ? BaseUrlProducao : BaseUrlHomologacao;

    /// <summary>Resolve o leiaute da DPS a partir do tpAmb — mesma regra de <see cref="ObterBaseUrl"/>.</summary>
    public LeiauteDps ObterLeiauteDps(string tpAmb) => tpAmb == "1" ? LeiauteDpsProducao : LeiauteDpsHomologacao;
}
