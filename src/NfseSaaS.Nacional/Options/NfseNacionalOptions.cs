namespace NfseSaaS.Nacional.Options;

/// <summary>
/// Configuração fortemente tipada da integração com a SEFIN Nacional,
/// vinculada via IOptions&lt;NfseNacionalOptions&gt; à seção "NfseNacional"
/// do appsettings. A URL é configurável sem recompilar a aplicação.
/// </summary>
public sealed class NfseNacionalOptions
{
    public const string SectionName = "NfseNacional";

    /// <summary>URL base da SEFIN Nacional (ex.: ambiente de produção restrita/homologação ou produção).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Identificação do ambiente ("Homologacao", "ProducaoRestrita", "Producao").</summary>
    public string Ambiente { get; set; } = "Homologacao";

    public int TimeoutSeconds { get; set; } = 60;
}
