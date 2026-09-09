using System.Globalization;
using System.Xml;

namespace NfseSaaS.Infrastructure.Danfse;

/// <summary>
/// Lê o XML da NFS-e autorizada e extrai, campo a campo, exatamente os
/// caminhos exigidos pela NT 008/2026 (item 2.4.5). Nunca lança por nó
/// ausente — DANFSe é gerado só a partir do que existe no XML (item 2.1:
/// "Não poderão ser impressas informações que não constem do arquivo da
/// NFS-e"), então ausência de nó é "campo vazio", não erro.
/// </summary>
internal sealed class DanfseXmlDados
{
    private const string Ns = "http://www.sped.fazenda.gov.br/nfse";
    private readonly XmlDocument _doc;
    private readonly XmlNamespaceManager _ns;

    public DanfseXmlDados(string xmlNfse)
    {
        _doc = new XmlDocument();
        _doc.LoadXml(xmlNfse);
        _ns = new XmlNamespaceManager(_doc.NameTable);
        _ns.AddNamespace("n", Ns);
    }

    private string? Texto(string caminho) => _doc.SelectSingleNode(caminho, _ns)?.InnerText;

    private bool Existe(string caminho) => _doc.SelectSingleNode(caminho, _ns) is not null;

    // ---- Cabeçalho / identificação do documento ----

    /// <summary>Chave de acesso SEM o prefixo "NFS" (item 2.1.1 / nota do item 2.4.5: "Informar o id da NFS-e sem o prefixo 'NFS'").</summary>
    public string ChaveAcesso()
    {
        var id = Texto("//n:NFSe/n:infNFSe/@Id") ?? Texto("//n:NFSe/n:infNFSe/n:id") ?? string.Empty;
        return id.StartsWith("NFS", StringComparison.OrdinalIgnoreCase) ? id[3..] : id;
    }

    /// <summary>Município + UF do emitente, pro cabeçalho (item 2.4.3). UF vem de infNFSe/emit/enderNac/UF — confirmado no XML real, não estava no caminho original que eu tinha usado.</summary>
    public string MunicipioEmitenteUf()
    {
        var municipio = Texto("//n:NFSe/n:infNFSe/n:xLocEmi") ?? "-";
        var uf = Texto("//n:NFSe/n:infNFSe/n:emit/n:enderNac/n:UF");
        return string.IsNullOrWhiteSpace(uf) ? municipio : $"{municipio} - {uf}";
    }

    /// <summary>
    /// Confirmado no PDF oficial: "Ambiente Gerador" e "Tipo de Ambiente"
    /// são impressos como o CÓDIGO CRU (não texto decodificado) — a NT
    /// 008 não lista "Utilizar a descrição" pra estes dois campos, ao
    /// contrário de tpEmit/cStat/opSimpNac etc., que são decodificados.
    /// </summary>
    public string AmbienteGerador() => Texto("//n:NFSe/n:infNFSe/n:ambGer") ?? "-";

    public string TipoAmbiente() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:tpAmb") ?? "-";

    /// <summary>tpAmb=2 (Homologação) ⇒ exibir "NFS-e SEM VALIDADE JURÍDICA" no cabeçalho (item 2.4.3).</summary>
    public bool AmbienteDeHomologacao() =>
        Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:tpAmb") == "2";

    public string NumeroNfse() => Texto("//n:NFSe/n:infNFSe/n:nNFSe") ?? "-";

    public string DataHoraProcessamento() => FormatarDataHora(Texto("//n:NFSe/n:infNFSe/n:dhProc"));

    public string Competencia() => FormatarData(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:dCompet"));

    public string NumeroDps() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:nDPS") ?? "-";

    public string SerieDps() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:serie") ?? "-";

    public string DataHoraEmissaoDps() => FormatarDataHora(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:dhEmi"));

    public string TipoEmitente() => CodigosNfse.DescricaoTpEmit(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:tpEmit"));

    public string Situacao() => CodigosNfse.DescricaoCStat(Texto("//n:NFSe/n:infNFSe/n:cStat"));

    public string Finalidade() => CodigosNfse.DescricaoFinNfse(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:IBSCBS/n:finNFSe"));

    // ---- Prestador ----

    public string PrestadorDocumento() => FormatarDocumento(
        Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:prest/n:CNPJ"),
        Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:prest/n:CPF"),
        Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:prest/n:NIF"));

    public string PrestadorInscricaoMunicipal() => Texto("//n:NFSe/n:infNFSe/n:emit/n:IM") ?? Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:prest/n:IM") ?? "-";
    public string PrestadorTelefone() => FormatarTelefone(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:prest/n:fone"));
    public string PrestadorNome() => CodigosNfse.Truncar(Texto("//n:NFSe/n:infNFSe/n:emit/n:xNome"), 77);
    public string PrestadorUf() => Texto("//n:NFSe/n:infNFSe/n:emit/n:enderNac/n:UF") ?? "-";
    public string PrestadorMunicipioUf() => $"{Texto("//n:NFSe/n:infNFSe/n:xLocEmi") ?? "-"} / {PrestadorUf()}";
    public string PrestadorCodigoIbgeCep() => $"{FormatarIbge(Texto("//n:NFSe/n:infNFSe/n:emit/n:enderNac/n:cMun"))} / {FormatarCep(Texto("//n:NFSe/n:infNFSe/n:emit/n:enderNac/n:CEP"))}";
    public string PrestadorEndereco() => CodigosNfse.Truncar(ConcatenarEndereco("//n:NFSe/n:infNFSe/n:emit/n:enderNac"), 77);
    public string PrestadorEmail() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:prest/n:email") ?? "-";
    public string PrestadorOpSimpNac() => CodigosNfse.DescricaoOpSimpNac(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:prest/n:regTrib/n:opSimpNac"));
    public string PrestadorRegApTribSN() => CodigosNfse.DescricaoRegApTribSN(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:prest/n:regTrib/n:regApTribSN"));
    public string PrestadorRegEspTrib() => CodigosNfse.DescricaoRegEspTrib(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:prest/n:regTrib/n:regEspTrib"));

    // ---- Tomador (Nota 2: bloco pode não existir) ----

    public bool TomadorExiste() => Existe("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma");
    public string TomadorDocumento() => FormatarDocumento(
        Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:CNPJ"),
        Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:CPF"),
        Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:NIF"));
    public string TomadorInscricaoMunicipal() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:IM") ?? "-";
    public string TomadorTelefone() => FormatarTelefone(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:fone"));
    public string TomadorNome() => CodigosNfse.Truncar(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:xNome"), 77);
    public string TomadorMunicipioUf()
    {
        // LIMITAÇÃO CONHECIDA: o XML só traz o CÓDIGO do município do
        // tomador (cMun), não o nome — nem sempre existe uma tag de nome
        // (xCidade) equivalente à do prestador (xLocEmi). Sem a tabela
        // completa de municípios do IBGE (~5570 linhas, não embarcada
        // aqui), não é possível exibir o NOME; a UF sai certa (2
        // primeiros dígitos do código), mas o município fica pelo
        // próprio código.
        var codigo = Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:end/n:endNac/n:cMun") ??
                     Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:end/n:endExt/n:xCidade");
        return $"Município IBGE {codigo ?? "-"} / {CodigosNfse.UfPorCodigoIbge(codigo)}";
    }
    public string TomadorCodigoIbgeCep() => $"{FormatarIbge(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:end/n:endNac/n:cMun"))} / {FormatarCep(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:end/n:endNac/n:CEP"))}";
    public string TomadorEndereco() => CodigosNfse.Truncar(ConcatenarEndereco("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:end"), 77);
    public string TomadorEmail() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:toma/n:email") ?? "-";

    // ---- Serviço prestado ----

    public string CodigoTributacaoNacionalMunicipal()
    {
        var nacional = FormatarCodigoTributacaoNacional(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:serv/n:cServ/n:cTribNac"));
        var municipal = Texto("//n:NFSe/n:infNFSe/n:cTribMun");
        return string.IsNullOrWhiteSpace(municipal) ? $"{nacional} / -" : $"{nacional} / {municipal}";
    }

    public string CodigoNbs() => FormatarNbs(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:serv/n:cServ/n:cNBS"));

    public string LocalPrestacao()
    {
        var municipio = Texto("//n:NFSe/n:infNFSe/n:xLocPrestacao") ?? "-";
        var pais = Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:serv/n:locPrest/n:cPaisPrestacao") ?? "-";
        return CodigosNfse.Truncar($"{municipio} / {PrestadorUf()} / {pais}", 42);
    }

    public string DescricaoTribNacionalMunicipal()
    {
        var municipal = Texto("//n:NFSe/n:infNFSe/n:xTribMun");
        var nacional = Texto("//n:NFSe/n:infNFSe/n:xTribNac");
        return CodigosNfse.Truncar(string.IsNullOrWhiteSpace(municipal) ? nacional : municipal, 167);
    }

    public string DescricaoServico() => CodigosNfse.Truncar(
        Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:serv/n:cServ/n:xDescServ"), 1297);

    // ---- Tributação Municipal (ISSQN) ----

    public bool IssqnAplicavel() => Existe("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:valores/n:trib/n:tribMun/n:tribISSQN");
    public string TipoTributacaoIssqn() => CodigosNfse.DescricaoTribIssqn(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:valores/n:trib/n:tribMun/n:tribISSQN"));
    public string MunicipioIncidenciaIssqn()
    {
        var municipio = Texto("//n:NFSe/n:infNFSe/n:xLocIncid") ?? PrestadorMunicipioUf().Split(" / ")[0];
        return CodigosNfse.Truncar($"{municipio} / {PrestadorUf()} / -", 42);
    }
    public string BaseCalculoIssqn() => FormatarMoeda(Texto("//n:NFSe/n:infNFSe/n:valores/n:vBC"));
    public string AliquotaIssqn() => FormatarPercentual(Texto("//n:NFSe/n:infNFSe/n:valores/n:pAliqAplic"));
    public string RetencaoIssqn() => CodigosNfse.DescricaoTpRetIssqn(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:valores/n:trib/n:tribMun/n:tpRetISSQN"));
    public string IssqnApurado() => FormatarMoeda(Texto("//n:NFSe/n:infNFSe/n:valores/n:vISSQN"));

    // ---- Tributação Federal (exceto CBS) ----

    public string Irrf() => FormatarMoeda(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:valores/n:trib/n:tribFed/n:vRetIRRF"));
    public string ContribuicaoPrevidenciariaRetida() => FormatarMoeda(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:valores/n:trib/n:tribFed/n:vRetCP"));
    public string ContribuicoesSociaisRetidas() => FormatarMoeda(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:valores/n:trib/n:tribFed/n:vRetCSLL"));
    public string PisDebitoApuracaoPropria() => FormatarMoeda(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:valores/n:trib/n:tribFed/n:piscofins/n:vPis"));
    public string CofinsDebitoApuracaoPropria() => FormatarMoeda(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:valores/n:trib/n:tribFed/n:piscofins/n:vCofins"));
    public string DescricaoContribSociaisRetidas() => CodigosNfse.DescricaoTpRetPisCofins(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:valores/n:trib/n:tribFed/n:piscofins/n:tpRetPisCofins"));

    // ---- Valor total ----

    public string ValorOperacaoServico() => FormatarMoeda(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:valores/n:vServPrest/n:vServ"));
    public string DescontoIncondicionado() => FormatarMoeda(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:valores/n:vDescCondIncond/n:vDescIncond"));
    public string DescontoCondicionado() => FormatarMoeda(Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:valores/n:vDescCondIncond/n:vDescCond"));
    public string TotalRetencoes() => FormatarMoeda(Texto("//n:NFSe/n:infNFSe/n:valores/n:vTotalRet"));
    public string ValorLiquido() => FormatarMoeda(Texto("//n:NFSe/n:infNFSe/n:valores/n:vLiq"));

    // ---- Totais aproximados de tributos (Lei 12.741/2012) — obrigatório, item 2.1.12/nota 10 ----

    public string TotaisAproximadosTributos()
    {
        // Formato EXATO confirmado no PDF oficial real: mesmo com
        // pTotTribSN preenchido no XML, o renderizador oficial mostrou
        // "-" nos 3 campos — aparentemente a SEFIN só usa esse texto pra
        // vTotTribFed/vTotTribEst/vTotTribMun (ou pTotTribFed/Est/Mun
        // separados), não pTotTribSN combinado do Simples Nacional. Como
        // este projeto só coleta pTotTribSN hoje, reproduzo o mesmo
        // comportamento (3 traços) em vez de inventar uma divisão que a
        // DPS não envia.
        return "Totais aproximados dos Tributos cfe. Lei n° 12.741/2012: Federais: -; Estaduais: -; Municipais: -;";
    }

    public string? ChaveNfseSubstituida() => Texto("//n:NFSe/n:infNFSe/n:DPS/n:infDPS/n:subst/n:chSubstda");

    // ---- Tributação IBS/CBS — confirmado no PDF real: bloco sempre
    // aparece, com "-" nos campos ausentes (não é suprimido como eu
    // tinha decidido antes de ver o PDF real). Este projeto ainda não
    // coleta o grupo IBSCBS na DPS (Reforma Tributária) — por isso tudo
    // aqui sai "-" por ora; ver nota completa em DanfseDocument.BlocoFederal. ----

    public string IbsCbsCstEClasseTrib() => "-";
    public string IbsCbsIndicadorOperacao() => "-";

    /// <summary>Confirmado no PDF real: este campo específico mostra "R$ 0,00", não "-", mesmo sem o grupo IBSCBS no XML (provavelmente por ser resultado de uma soma, que dá zero em vez de "ausente").</summary>
    public string IbsCbsExclusoesReducoesBc() => $"R$ {FormatarMoedaComZero(null)}";
    public string IbsCbsBaseCalculo() => "-";
    public string IbsCbsReducaoAliquota() => "-";
    public string IbsCbsAliquotaUfMun() => "-";
    public string IbsCbsAliquotaEfetivaMunicipal() => "-";
    public string IbsCbsValorApuradoMunicipal() => "-";
    public string IbsCbsAliquotaEfetivaEstadual() => "-";
    public string IbsCbsValorApuradoEstadual() => "-";
    public string IbsCbsValorTotalApuradoIbs() => FormatarMoeda(Texto("//n:NFSe/n:infNFSe/n:IBSCBS/n:totCIBS/n:gIBS/n:vIBSTot"));
    public string IbsCbsAliquota() => "-";
    public string IbsCbsAliquotaEfetiva() => "-";
    public string IbsCbsValorTotalApuradoCbs() => FormatarMoeda(Texto("//n:NFSe/n:infNFSe/n:IBSCBS/n:totCIBS/n:gCBS/n:vCBS"));

    /// <summary>Confirmado no PDF real: "Total do IBS/CBS" e "Valor Líquido + IBS/CBS" mostram "R$ 0,00" (não "-") quando o grupo IBSCBS não existe.</summary>
    public string TotalIbsCbs() => FormatarMoedaComZero(null);
    public string ValorLiquidoMaisIbsCbs() => FormatarMoedaComZero(Texto("//n:NFSe/n:infNFSe/n:IBSCBS/n:totCIBS/n:vTotNF"));

    // ---- Helpers de formatação ----

    private static string FormatarDocumento(string? cnpj, string? cpf, string? nif)
    {
        if (!string.IsNullOrWhiteSpace(cnpj) && cnpj.Length == 14)
            return $"{cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..14]}";
        if (!string.IsNullOrWhiteSpace(cpf) && cpf.Length == 11)
            return $"{cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..11]}";
        return nif ?? "-";
    }

    private string ConcatenarEndereco(string caminhoEnd)
    {
        var lgr = Texto($"{caminhoEnd}/n:xLgr");
        var nro = Texto($"{caminhoEnd}/n:nro");
        var cpl = Texto($"{caminhoEnd}/n:xCpl");
        var bairro = Texto($"{caminhoEnd}/n:xBairro");
        return string.Join(", ", new[] { lgr, nro, cpl, bairro }.Where(v => !string.IsNullOrWhiteSpace(v)));
    }

    private static string FormatarData(string? valor) =>
        DateTime.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)
            ? data.ToString("dd/MM/yyyy")
            : "-";

    private static string FormatarDataHora(string? valor) =>
        DateTimeOffset.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)
            ? data.ToString("dd/MM/yyyy HH:mm:ss")
            : "-";

    private static string FormatarMoeda(string? valor) =>
        decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var numero) && numero != 0
            ? numero.ToString("N2", new CultureInfo("pt-BR"))
            : "-";

    private static string FormatarMoedaComZero(string? valor) =>
        (decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var numero) ? numero : 0m)
            .ToString("N2", new CultureInfo("pt-BR"));

    private static string FormatarPercentual(string? valor) =>
        decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var numero) && numero != 0
            ? numero.ToString("N2", new CultureInfo("pt-BR")) + "%"
            : "-";

    /// <summary>"010701" → "01.07.01" — confirmado no PDF oficial real.</summary>
    private static string FormatarCodigoTributacaoNacional(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor) || valor.Length != 6)
            return valor ?? "-";
        return $"{valor[..2]}.{valor[2..4]}.{valor[4..6]}";
    }

    /// <summary>"115013000" (9 dígitos) → "1.1501.30.00" — confirmado no PDF oficial real.</summary>
    private static string FormatarNbs(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor) || valor.Length != 9)
            return valor ?? "-";
        return $"{valor[..1]}.{valor[1..5]}.{valor[5..7]}.{valor[7..9]}";
    }

    /// <summary>"2919207" (7 dígitos, código IBGE) → "29.19207" — confirmado no PDF oficial real.</summary>
    private static string FormatarIbge(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor) || valor.Length != 7)
            return valor ?? "-";
        return $"{valor[..2]}.{valor[2..7]}";
    }

    /// <summary>"42700530" (8 dígitos) → "42.700-530" — confirmado no PDF oficial real.</summary>
    private static string FormatarCep(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor) || valor.Length != 8)
            return valor ?? "-";
        return $"{valor[..2]}.{valor[2..5]}-{valor[5..8]}";
    }

    /// <summary>"7135085171" (10-11 dígitos, DDD+número) → "(71) 3508-5171" — confirmado no PDF oficial real.</summary>
    private static string FormatarTelefone(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return "-";

        var digitos = new string(valor.Where(char.IsDigit).ToArray());
        return digitos.Length switch
        {
            10 => $"({digitos[..2]}) {digitos[2..6]}-{digitos[6..10]}",
            11 => $"({digitos[..2]}) {digitos[2..7]}-{digitos[7..11]}",
            _ => valor
        };
    }
}
