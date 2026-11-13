using NfseSaaS.Application.Exceptions;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.DocumentosEmpresa;

/// <summary>De onde veio o link oficial que a empresa vê.</summary>
public enum OrigemLinkDocumento
{
    /// <summary>A própria empresa cadastrou.</summary>
    Empresa = 1,
    /// <summary>O operador cadastrou para o município da empresa.</summary>
    Municipio = 2,
    /// <summary>O operador cadastrou para a UF da empresa.</summary>
    Estado = 3,
    /// <summary>Outras empresas do mesmo município/UF usam (a empresa confirma com "Usar este link").</summary>
    Sugerido = 4,
    /// <summary>O link geral do catálogo (federais).</summary>
    Catalogo = 5
}

/// <param name="Texto">Explicação curta para a tela ("Link que você cadastrou"...).</param>
/// <param name="Dominio">Só o endereço do site (ex.: "www.sefaz.ba.gov.br"), para a pessoa saber onde vai cair.</param>
public sealed record LinkDocumentoResolvido(string Link, OrigemLinkDocumento Origem, string Texto, string Dominio);

/// <summary>Link usado por empresas de um município/UF (para sugerir e para o operador oficializar).</summary>
public sealed record UsoLinkDocumento(string Link, int Contas);

/// <summary>
/// DOC4: links oficiais dos documentos. Ordem: o da empresa → o do
/// município (operador) → o do estado (operador) → o sugerido por outras
/// empresas do mesmo lugar → o geral do catálogo. Só https.
/// </summary>
public static class RegrasLinkDocumento
{
    public const int TamanhoMaximo = 500;

    /// <summary>Sugestão vinda das empresas só aparece com pelo menos tantas contas usando, ou se for site do governo (.gov.br).</summary>
    public const int ContasMinimasSugestao = 2;

    /// <summary>Vazio = sem link. Senão, precisa ser https e ter um endereço de site válido.</summary>
    public static string? Normalizar(string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
            return null;

        var texto = link.Trim();
        if (texto.Length > TamanhoMaximo)
            throw new RegraNegocioException($"O link pode ter até {TamanhoMaximo} caracteres.");
        if (!texto.Contains("://", StringComparison.Ordinal))
            texto = "https://" + texto;
        if (!Uri.TryCreate(texto, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(uri.Host)
            || !uri.Host.Contains('.')
            || !string.IsNullOrEmpty(uri.UserInfo))
            throw new RegraNegocioException("Informe o link completo do site oficial, começando com https://");
        return uri.AbsoluteUri;
    }

    public static string Dominio(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri) ? uri.Host : link;

    /// <summary>Site de governo: pode ser sugerido mesmo com uma conta só.</summary>
    public static bool EhGoverno(string link) =>
        Dominio(link).EndsWith(".gov.br", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A sugestão mais usada no lugar que passa no filtro (governo ou
    /// várias contas). Um link de uma conta só, fora de .gov.br, não é
    /// espalhado para as outras — evita que alguém plante um link falso.
    /// </summary>
    public static string? MelhorSugestao(IEnumerable<UsoLinkDocumento> usos) =>
        usos.Where(u => u.Contas >= ContasMinimasSugestao || EhGoverno(u.Link))
            .OrderByDescending(u => u.Contas)
            .ThenByDescending(u => EhGoverno(u.Link))
            .ThenBy(u => u.Link, StringComparer.Ordinal)
            .Select(u => u.Link)
            .FirstOrDefault();

    /// <param name="nomeLocal">Município ou UF da empresa, para o texto da sugestão.</param>
    public static LinkDocumentoResolvido? Resolver(
        TipoDocumentoInfo info, string? linkProprio, string? linkMunicipio, string? linkEstado, string? sugerido, string? nomeLocal)
    {
        if (!string.IsNullOrWhiteSpace(linkProprio))
            return Novo(linkProprio, OrigemLinkDocumento.Empresa, "Link que você cadastrou");

        if (info.Abrangencia == AbrangenciaDocumento.Municipal && !string.IsNullOrWhiteSpace(linkMunicipio))
            return Novo(linkMunicipio, OrigemLinkDocumento.Municipio, $"Site oficial{De(nomeLocal)}");
        if (info.Abrangencia == AbrangenciaDocumento.Estadual && !string.IsNullOrWhiteSpace(linkEstado))
            return Novo(linkEstado, OrigemLinkDocumento.Estado, $"Site oficial{De(nomeLocal)}");

        if (info.Abrangencia != AbrangenciaDocumento.Nacional && !string.IsNullOrWhiteSpace(sugerido))
            return Novo(sugerido, OrigemLinkDocumento.Sugerido, $"Usado por outras empresas{De(nomeLocal)}");

        return string.IsNullOrWhiteSpace(info.LinkEmissao)
            ? null
            : Novo(info.LinkEmissao, OrigemLinkDocumento.Catalogo, $"Site oficial{(string.IsNullOrWhiteSpace(info.Orgao) || info.Orgao == "—" ? "" : $" ({info.Orgao})")}");
    }

    private static string De(string? nomeLocal) => string.IsNullOrWhiteSpace(nomeLocal) ? "" : $" de {nomeLocal}";

    private static LinkDocumentoResolvido Novo(string link, OrigemLinkDocumento origem, string texto) =>
        new(link, origem, texto, Dominio(link));
}
