using Microsoft.Extensions.Options;
using System.Xml;
using NfseSaaS.Nacional.Helpers;
using NfseSaaS.Nacional.Models;
using NfseSaaS.Nacional.Options;

namespace NfseSaaS.Nacional.Builders;

/// <summary>
/// Monta o XML (ainda NÃO assinado) do evento de cancelamento de NFS-e
/// (e101101), leiaute pedRegEvento/infPedReg (schema 1.00) da SEFIN
/// Nacional.
///
/// ATENÇÃO — diferença em relação a DpsBuilder: a estrutura abaixo foi
/// reconstruída a partir do Swagger oficial (endpoint/JSON de transporte,
/// esses sim 100% confirmados: POST /nfse/{chaveAcesso}/eventos, body
/// { pedidoRegistroEventoXmlGZipB64 }) combinado com exemplos reais de
/// XML publicados por terceiros — NÃO foi validada com uma emissão real
/// deste projeto (ao contrário de DpsBuilder/DpsSigner, que já têm uma
/// emissão real aceita pela SEFIN em produção restrita).
/// TESTAR EM HOMOLOGAÇÃO e comparar contra o XSD oficial antes de usar em
/// produção — se a SEFIN devolver erro de schema (campo faltando/a mais),
/// é aqui que ajusta.
/// </summary>
public sealed class EventoCancelamentoBuilder : IEventoCancelamentoBuilder
{
    private const string Ns = "http://www.sped.fazenda.gov.br/nfse";

    // Código do evento "Cancelamento de NFS-e" na tabela oficial de
    // eventos da NFS-e Nacional (confirmado no enum do Swagger oficial:
    // tipoEvento ∈ {101101, 101103, 105102, ...}). Distinto de 105102
    // (cancelamento por substituição), que é restrito a sistemas
    // municipais conveniados — não se aplica a este SaaS.
    private const string TipoEvento = "101101";

    private readonly NfseNacionalOptions _options;

    public EventoCancelamentoBuilder(IOptions<NfseNacionalOptions> options)
    {
        _options = options.Value;
    }

    public (string XmlEvento, string InfPedRegId) Construir(EventoCancelamentoRequest request)
    {
        var numeroPedido = request.NumeroPedidoRegistroEvento.ToString("D3");

        // Id = "PRE" + chNFSe + tipoEvento + nPedRegEvento (3 dígitos),
        // conforme exemplo oficial de integração.
        var infPedRegId = "PRE" + request.ChaveAcesso + TipoEvento + numeroPedido;

        var doc = new XmlDocument { PreserveWhitespace = true };

        var pedRegEvento = doc.CreateElement("pedRegEvento", Ns);
        pedRegEvento.SetAttribute("versao", "1.00");
        doc.AppendChild(pedRegEvento);

        var infPedReg = doc.CreateElement("infPedReg", Ns);
        infPedReg.SetAttribute("Id", infPedRegId);
        pedRegEvento.AppendChild(infPedReg);

        Add(doc, infPedReg, "tpAmb", MapearTpAmb(_options.Ambiente));
        Add(doc, infPedReg, "verAplic", "NfseSaaS_1.0");
        Add(doc, infPedReg, "CNPJAutor", request.CnpjAutor);
        Add(doc, infPedReg, "chNFSe", request.ChaveAcesso);
        Add(doc, infPedReg, "nPedRegEvento", numeroPedido);

        var e101101 = AddNode(doc, infPedReg, "e101101");
        Add(doc, e101101, "xDesc", "Cancelamento de NFS-e");
        Add(doc, e101101, "cMotivo", request.CodigoMotivo.ToString());
        Add(doc, e101101, "xMotivo", request.Motivo);

        return (XmlSerializationHelper.ToXmlString(doc), infPedRegId);
    }

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
