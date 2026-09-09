using System.Globalization;
using System.Xml;

namespace NfseSaaS.Infrastructure.SincronizacaoSefin;

/// <summary>
/// Extrai os campos do XML de uma NFS-e em formato BRUTO (sem máscara,
/// sem decodificação de texto) — usado pra gravar no banco ao importar
/// uma nota vinda de outro canal (Distribuição de DF-e/ADN). Complementa
/// DanfseXmlDados (Infrastructure.Danfse), que existe pro caso oposto:
/// formatar pra exibição num PDF. Os dois leem o mesmo XML, mas com
/// propósitos e formatos de saída diferentes — não foi reaproveitado de
/// propósito, pra não acoplar "dado pra gravar" a "dado pra mostrar".
/// </summary>
internal sealed class NfseXmlImportDados
{
    private const string Ns = "http://www.sped.fazenda.gov.br/nfse";
    private readonly XmlDocument _doc;
    private readonly XmlNamespaceManager _ns;

    public NfseXmlImportDados(string xmlNfse)
    {
        _doc = new XmlDocument();
        _doc.LoadXml(xmlNfse);
        _ns = new XmlNamespaceManager(_doc.NameTable);
        _ns.AddNamespace("n", Ns);
    }

    private string? Texto(string caminho) => _doc.SelectSingleNode(caminho, _ns)?.InnerText;

    public string CStat() => Texto("//n:NFSe/n:infNFSe/n:cStat") ?? string.Empty;

    public int NumeroDps() => int.TryParse(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:nDPS"), out var n) ? n : 0;

    public string SerieDps() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:serie") ?? string.Empty;

    public string? NumeroNfse() => Texto("//n:NFSe/n:infNFSe/n:nNFSe");

    public DateOnly DataCompetencia() =>
        DateOnly.TryParse(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:dCompet"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : DateOnly.FromDateTime(DateTime.UtcNow);

    public DateTimeOffset? DataEmissao() =>
    DateTimeOffset.TryParse(Texto("//n:NFSe/n:infNFSe/n:dhProc"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
        ? d.ToUniversalTime()
        : null;

    public decimal ValorServico() =>
        decimal.TryParse(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:valores/n:vServPrest/n:vServ"), NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
            ? v
            : 0m;

    public decimal? ValorLiquido() =>
        decimal.TryParse(Texto("//n:NFSe/n:infNFSe/n:valores/n:vLiq"), NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;

    public string DescricaoServico() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:serv/n:cServ/n:xDescServ") ?? string.Empty;

    public string CodigoTributacaoNacional() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:serv/n:cServ/n:cTribNac") ?? string.Empty;

    public string CodigoNbs() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:serv/n:cServ/n:cNBS") ?? string.Empty;

    public string TribIssqn() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:valores/n:trib/n:tribMun/n:tribISSQN") ?? string.Empty;

    public string TpRetIssqn() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:valores/n:trib/n:tribMun/n:tpRetISSQN") ?? string.Empty;

    public string CstPisCofins() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:valores/n:trib/n:tribFed/n:piscofins/n:CST") ?? string.Empty;

    public string TpRetPisCofins() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:valores/n:trib/n:tribFed/n:piscofins/n:tpRetPisCofins") ?? string.Empty;

    public string PercentualTotalTributosSimplesNacional() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:valores/n:trib/n:totTrib/n:pTotTribSN") ?? string.Empty;

    // ---- Tomador (pra find-or-create de Cliente) ----

    public string TomadorCpfCnpj()
    {
        var cnpj = Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:CNPJ");
        if (!string.IsNullOrWhiteSpace(cnpj)) return cnpj;
        return Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:CPF") ?? string.Empty;
    }

    public string TomadorNome() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:xNome") ?? string.Empty;
    public string? TomadorEmail() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:email");
    public string? TomadorTelefone() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:fone");
    public string TomadorCodigoMunicipio() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:end/n:endNac/n:cMun") ?? string.Empty;
    public string TomadorCep() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:end/n:endNac/n:CEP") ?? string.Empty;
    public string TomadorLogradouro() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:end/n:xLgr") ?? string.Empty;
    public string TomadorNumero() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:end/n:nro") ?? string.Empty;
    public string? TomadorComplemento() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:end/n:xCpl");
    public string TomadorBairro() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:end/n:xBairro") ?? string.Empty;
}
