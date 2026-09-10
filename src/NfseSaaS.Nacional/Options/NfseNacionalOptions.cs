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

    /// <summary>Resolve a BaseUrl certa a partir do tpAmb ("1" = Produção, "2" = Homologação).</summary>
    public string ObterBaseUrl(string tpAmb) => tpAmb == "1" ? BaseUrlProducao : BaseUrlHomologacao;
}
