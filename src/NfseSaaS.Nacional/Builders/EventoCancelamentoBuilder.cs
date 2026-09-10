using System.Xml;
using NfseSaaS.Nacional.Helpers;
using NfseSaaS.Nacional.Models;

namespace NfseSaaS.Nacional.Builders;

/// <summary>
/// Monta o XML (ainda NÃO assinado) do evento de cancelamento de NFS-e
/// (e101101), leiaute pedRegEvento/infPedReg (schema 1.00) da SEFIN
/// Nacional.
///
/// Testado contra uma rejeição real (E1235, "Falha no esquema XML do
/// DF-e") em 09/09/2026 — a primeira versão deste builder usava um
/// leiaute de Id/campos DESATUALIZADO. A SEFIN Nacional mudou esse
/// leiaute em 27/12/2025 (NT dos Eventos Anexo II): o campo
/// nPedRegEvento foi removido por completo do XML, e o atributo Id de
/// infPedReg encurtou de 62 para 59 caracteres (deixou de incluir
/// nPedRegEvento na composição). Também faltava o campo dhEvento
/// (obrigatório). Corrigido abaixo; ainda assim, TESTAR de novo em
/// homologação antes de confiar — o log da SEFIN (via NfseApiClient)
/// mostra o corpo bruto de qualquer rejeição nova.
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

    public (string XmlEvento, string InfPedRegId) Construir(EventoCancelamentoRequest request)
    {
        if (request.TpAmb is not ("1" or "2"))
            throw new ArgumentException($"TpAmb inválido: '{request.TpAmb}'. Esperado \"1\" (Produção) ou \"2\" (Homologação).", nameof(request));

        // Id = "PRE" + chNFSe (50) + tipoEvento (6) = 59 caracteres.
        // NÃO inclui mais nPedRegEvento (removido do leiaute em
        // 27/12/2025) — confirmado contra o padrão oficial
        // "PRE[0-9]{56}" (TSIdPedRegEvt) e contra a rejeição real E1235
        // que apontou exatamente esse Id como inválido.
        var infPedRegId = "PRE" + request.ChaveAcesso + TipoEvento;

        var agora = DateTimeOffset.Now;

        var doc = new XmlDocument { PreserveWhitespace = true };

        var pedRegEvento = doc.CreateElement("pedRegEvento", Ns);
        pedRegEvento.SetAttribute("versao", "1.00");
        doc.AppendChild(pedRegEvento);

        var infPedReg = doc.CreateElement("infPedReg", Ns);
        infPedReg.SetAttribute("Id", infPedRegId);
        pedRegEvento.AppendChild(infPedReg);

        // Ordem dos campos importa — infPedReg é um "sequence" no XSD,
        // e enviar fora de ordem já causou rejeição em outros sistemas
        // (mesma NT de 27/12/2025).
        Add(doc, infPedReg, "tpAmb", request.TpAmb);
        Add(doc, infPedReg, "verAplic", "NfseSaaS_1.0");
        Add(doc, infPedReg, "dhEvento", agora.ToString("yyyy-MM-ddTHH:mm:sszzz"));
        Add(doc, infPedReg, "CNPJAutor", request.CnpjAutor);
        Add(doc, infPedReg, "chNFSe", request.ChaveAcesso);

        var e101101 = AddNode(doc, infPedReg, "e101101");
        Add(doc, e101101, "xDesc", "Cancelamento de NFS-e");
        Add(doc, e101101, "cMotivo", request.CodigoMotivo.ToString());
        Add(doc, e101101, "xMotivo", request.Motivo);

        return (XmlSerializationHelper.ToXmlString(doc), infPedRegId);
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
        // Espaço em branco sobrando no início/fim quebra o Pattern de
        // vários tipos do schema da SEFIN Nacional (confirmado contra
        // uma rejeição real, E1235 "Pattern constraint failed" no
        // TSMotivo) — mais barato aparar aqui, uma vez, do que confiar
        // que todo texto que chega até um builder já vem limpo.
        element.InnerText = valor.Trim();
        parent.AppendChild(element);
    }
}
