using System.Globalization;
using System.Xml;
using NfseSaaS.Nacional.Helpers;
using NfseSaaS.Nacional.Models;

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
///
/// O tpAmb (request.TpAmb) TAMBÉM vem de fora agora — este builder não
/// lê mais NfseNacionalOptions/appsettings pra decidir isso. Antes era
/// um valor global (mesmo ambiente fiscal pra toda empresa do sistema);
/// hoje é decidido por Empresa (Empresa.TipoAmbiente), resolvido em
/// EmitirNfseUseCase antes de chegar aqui.
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

    public (string XmlDps, string InfDpsId) Construir(DpsRequest request)
    {
        if (request.TpAmb is not ("1" or "2"))
            throw new ArgumentException($"TpAmb inválido: '{request.TpAmb}'. Esperado \"1\" (Produção) ou \"2\" (Homologação).", nameof(request));

        // DateTimeOffset.Now usa o fuso do SERVIDOR, não o de Brasília — em
        // produção (container Linux/Docker) o fuso do SO é UTC, então
        // .Now vem com offset "+00:00". O dhEmi acaba correto em termos de
        // INSTANTE (mesmo ponto no tempo), mas a SEFIN rejeitou com E0008
        // ("dhEmi posterior ao processamento") mesmo assim — sinal de que
        // ela não normaliza o offset recebido antes de comparar, e lê os
        // dígitos do relógio como se já fossem hora de Brasília. Resultado:
        // um dhEmi em UTC aparenta estar ~3h no "futuro" da perspectiva
        // dela. Construir explicitamente em -03:00 (fixo, sem horário de
        // verão desde 2019 — mesma premissa já usada em
        // ListarNfseUseCase.offsetBrasilia) elimina essa ambiguidade
        // independente do fuso do servidor onde isto roda.
        var agora = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3)) - MargemSegurancaDhEmi;

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

        Add(doc, infDps, "tpAmb", request.TpAmb);
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

        // <totTrib> só existe pra carregar pTotTribSN — se não tem valor
        // (Empresa Não optante, "1"), omite o grupo inteiro em vez de
        // deixar <totTrib></totTrib> vazio; mesmo raciocínio do pTotTribSN
        // abaixo, aplicado um nível acima por precaução.
        if (!string.IsNullOrWhiteSpace(request.Tributacao.PercentualTotalTributosSimplesNacional))
        {
            var totTrib = AddNode(doc, trib, "totTrib");

            // pTotTribSN é opcional de verdade no XSD (minOccurs=0) pra
            // Empresa Não optante ("1") — mesma regra que já vale no
            // DpsValidator. Se o valor vier vazio, o elemento NÃO pode
            // aparecer no XML: um <pTotTribSN></pTotTribSN> vazio quebra
            // o Pattern do tipo TSDec2V2 na SEFIN (E1235 "Falha no esquema
            // XML do DF-e"), mesmo a validação local deixando passar por
            // considerar o campo dispensável nesse caso.
            Add(doc, totTrib, "pTotTribSN", request.Tributacao.PercentualTotalTributosSimplesNacional);
        }

        return (XmlSerializationHelper.ToXmlString(doc), infDpsId);
    }

    private static XmlElement AddNode(XmlDocument doc, XmlElement parent, string nome)
    {
        var element = doc.CreateElement(nome, Ns);
        parent.AppendChild(element);
        return element;
    }

    private static void Add(XmlDocument doc, XmlElement parent, string nome, string valor)
    {
        var element = doc.CreateElement(nome, Ns);
        // Mesmo motivo do Add() em EventoCancelamentoBuilder: espaço
        // sobrando no início/fim quebra o Pattern de vários tipos do
        // schema da SEFIN — aparado aqui, uma vez, pra qualquer campo de
        // texto livre da DPS (descrição do serviço, nomes, endereço).
        element.InnerText = valor.Trim();
        parent.AppendChild(element);
    }
}