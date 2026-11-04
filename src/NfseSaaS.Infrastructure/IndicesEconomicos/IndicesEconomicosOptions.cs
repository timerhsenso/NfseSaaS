namespace NfseSaaS.Infrastructure.IndicesEconomicos;

/// <summary>
/// Seção "IndicesEconomicos" do appsettings. Tudo tem default — a seção
/// só precisa existir para mudar algo (ex.: desligar a importação num
/// ambiente sem saída para a internet).
/// </summary>
public sealed class IndicesEconomicosOptions
{
    public const string SectionName = "IndicesEconomicos";

    /// <summary>Raiz da API SGS (dados abertos do Banco Central, sem autenticação).</summary>
    public string BaseUrl { get; set; } = "https://api.bcb.gov.br/dados/serie/";

    /// <summary>Quantos meses buscar na primeira importação de um índice (tabela vazia).</summary>
    public int MesesHistorico { get; set; } = 24;

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>false: não agenda o job diário (o botão "Importar agora" continua funcionando).</summary>
    public bool ImportacaoAutomatica { get; set; } = true;

    /// <summary>
    /// Código SGS de cada índice (chave = nome do enum IndiceReajusteContrato).
    /// Variação MENSAL em %. Selic e CDI usam a taxa acumulada no mês.
    /// Dá para trocar um código pelo appsettings sem recompilar
    /// (ex.: "IndicesEconomicos:Series:Incc": 7456).
    /// </summary>
    public Dictionary<string, int> Series { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ipca"] = 433,
        ["Igpm"] = 189,
        ["Incc"] = 192,
        ["Inpc"] = 188,
        ["Selic"] = 4390,
        ["Cdi"] = 4391
    };
}
