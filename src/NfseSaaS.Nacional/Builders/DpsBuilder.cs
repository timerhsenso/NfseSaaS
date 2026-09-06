using System.Globalization;
using System.Xml;
using Microsoft.Extensions.Options;
using NfseSaaS.Nacional.Helpers;
using NfseSaaS.Nacional.Models;
using NfseSaaS.Nacional.Options;

namespace NfseSaaS.Nacional.Builders;

/// <summary>
/// Monta o XML (ainda NÃO assinado) da DPS. Migrado de
/// NfsePoc/DpsBuilder.cs (método Gerar) — mesmo layout, namespace do SPED,
/// faixa de série por tpEmit e margem de segurança no dhEmi já validados
/// numa emissão real aceita pela SEFIN (HTTP 201).
///
/// Diferença central em relação à POC: TODOS os dados de prestador,
/// tomador e tributação agora vêm de <see cref="DpsRequest"/> — a POC
/// tinha um único prestador fixo no código, o que não faz sentido num SaaS
/// multiempresa.
/// </summary>
public sealed class DpsBuilder : IDpsBuilder
{
    private const string Ns = "http://www.sped.fazenda.gov.br/nfse";

    // tpEmit=1 (emissão com aplicativo próprio) exige série na faixa
    // 00001-49999 — ver manual da NFS-e Nacional. A validação dessa faixa
    // é responsabilidade de IDpsValidator, não deste builder.
    private const string TpEmit = "1";

    // 2 = CNPJ. Fixo porque Empresa, no Domain deste SaaS, sempre se
    // identifica por CNPJ (não há emissão por CPF neste sistema).
    private const string TpInscPrestadorCnpj = "2";

    // Margem de segurança subtraída do dhEmi para absorver latência de rede
    // e o desvio residual de relógio entre este servidor e o da SEFIN —
    // comprovadamente necessária na POC para evitar a rejeição E0008
    // ("A data de emissão da DPS não pode ser posterior à data do seu
    // processamento").
    private static readonly TimeSpan MargemSegurancaDhEmi = TimeSpan.FromSeconds(5);

    private readonly NfseNacionalOptions _options;

    public DpsBuilder(IOptions<NfseNacionalOptions> options)
    {
        _options = options.Value;
    }

    public (string XmlDps, string InfDpsId) Construir(DpsRequest request)
    {
        var agora = DateTimeOffset.Now - MargemSegurancaDhEmi;

        var infDpsId =
            "DPS" +
            request.Prestador.CodigoMunicipio +
            TpInscPrestadorCnpj +
            request.Prestador.Cnpj +
            request.SerieDps +
            request.NumeroDps.ToString("D15");

        var doc = new XmlDocument { PreserveWhitespace = true };

        var dps = doc.CreateElement("DPS", Ns);
        dps.SetAttribute("versao", "1.01");
        doc.AppendChild(dps);

        var infDps = doc.CreateElement("infDPS", Ns);
        infDps.SetAttribute("Id", infDpsId);
        dps.AppendChild(infDps);

        Add(doc, infDps, "tpAmb", MapearTpAmb(_options.Ambiente));
        Add(doc, infDps, "dhEmi", agora.ToString("yyyy-MM-ddTHH:mm:sszzz"));
        Add(doc, infDps, "verAplic", "NfseSaaS_1.0");
        Add(doc, infDps, "serie", request.SerieDps);
        Add(doc, infDps, "nDPS", request.NumeroDps.ToString(CultureInfo.InvariantCulture));
        Add(doc, infDps, "dCompet", request.DataCompetencia.ToString("yyyy-MM-dd"));
        Add(doc, infDps, "tpEmit", TpEmit);
        Add(doc, infDps, "cLocEmi", request.Prestador.CodigoMunicipio);

        var prest = AddNode(doc, infDps, "prest");
        Add(doc, prest, "CNPJ", request.Prestador.Cnpj);
        Add(doc, prest, "IM", request.Prestador.InscricaoMunicipal);
        Add(doc, prest, "fone", request.Prestador.Telefone);
        Add(doc, prest, "email", request.Prestador.Email);

        var regTrib = AddNode(doc, prest, "regTrib");
        Add(doc, regTrib, "opSimpNac", request.Prestador.OpSimpNac);
        Add(doc, regTrib, "regApTribSN", request.Prestador.RegApTribSN);
        Add(doc, regTrib, "regEspTrib", request.Prestador.RegEspTrib);

        var toma = AddNode(doc, infDps, "toma");
        Add(doc, toma, "CNPJ", request.Tomador.CnpjOuCpf);
        Add(doc, toma, "xNome", request.Tomador.Nome);

        var end = AddNode(doc, toma, "end");
        var endNac = AddNode(doc, end, "endNac");
        Add(doc, endNac, "cMun", request.Tomador.CodigoMunicipio);
        Add(doc, endNac, "CEP", request.Tomador.Cep);
        Add(doc, end, "xLgr", request.Tomador.Logradouro);
        Add(doc, end, "nro", request.Tomador.Numero);
        Add(doc, end, "xBairro", request.Tomador.Bairro);

        var serv = AddNode(doc, infDps, "serv");
        var locPrest = AddNode(doc, serv, "locPrest");
        Add(doc, locPrest, "cLocPrestacao", request.Prestador.CodigoMunicipio);

        var cServ = AddNode(doc, serv, "cServ");
        Add(doc, cServ, "cTribNac", request.CodigoTributacaoNacional);
        Add(doc, cServ, "xDescServ", request.DescricaoServico);
        Add(doc, cServ, "cNBS", request.CodigoNbs);

        var valores = AddNode(doc, infDps, "valores");
        var vServPrest = AddNode(doc, valores, "vServPrest");
        Add(doc, vServPrest, "vServ", request.Valor.ToString("0.00", CultureInfo.InvariantCulture));

        var trib = AddNode(doc, valores, "trib");
        var tribMun = AddNode(doc, trib, "tribMun");
        Add(doc, tribMun, "tribISSQN", request.Tributacao.TribIssqn);
        Add(doc, tribMun, "tpRetISSQN", request.Tributacao.TpRetIssqn);

        var tribFed = AddNode(doc, trib, "tribFed");
        var piscofins = AddNode(doc, tribFed, "piscofins");
        Add(doc, piscofins, "CST", request.Tributacao.CstPisCofins);
        Add(doc, piscofins, "tpRetPisCofins", request.Tributacao.TpRetPisCofins);

        var totTrib = AddNode(doc, trib, "totTrib");
        Add(doc, totTrib, "pTotTribSN", request.Tributacao.PercentualTotalTributosSimplesNacional);

        return (XmlSerializationHelper.ToXmlString(doc), infDpsId);
    }

    /// <summary>
    /// tpAmb: 1 = Produção, 2 = Homologação. "ProducaoRestrita" (ambiente
    /// de testes da SEFIN usado na POC) também mapeia para 2 — foi o valor
    /// usado na emissão real que obteve HTTP 201.
    /// </summary>
    private static string MapearTpAmb(string ambiente) => ambiente switch
    {
        "Producao" => "1",
        "Homologacao" => "2",
        "ProducaoRestrita" => "2",
        _ => "2"
    };

    private static XmlElement AddNode(XmlDocument doc, XmlElement parent, string nome)
    {
        var element = doc.CreateElement(nome, Ns);
        parent.AppendChild(element);
        return element;
    }

    private static void Add(XmlDocument doc, XmlElement parent, string nome, string valor)
    {
        var element = doc.CreateElement(nome, Ns);
        element.InnerText = valor;
        parent.AppendChild(element);
    }
}