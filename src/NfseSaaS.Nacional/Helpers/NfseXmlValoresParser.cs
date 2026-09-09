using System.Globalization;
using System.Xml;

namespace NfseSaaS.Nacional.Helpers;

/// <summary>
/// Extrai o valor líquido (vLiq) do XML da NFS-e autorizada, retornado
/// pela SEFIN Nacional após o envio da DPS (caminho no XML:
/// NFSe/infNFSe/valores/vLiq).
///
/// Deliberadamente só vLiq é extraído aqui. O restante do bloco
/// "valores" — retenções federais (vRetCP/vRetIRRF/vRetCSLL), base de
/// cálculo e alíquota do ISSQN, descontos condicionado/incondicionado, e
/// — a partir de 2026 — os grupos de IBS/CBS da Reforma Tributária (NT
/// 002/004) — tem estrutura aninhada, condicional ao regime tributário
/// do emitente, e ainda em evolução. Não se presta a colunas fixas do
/// tipo ValorIss/ValorPis/ValorCofins: fica reservado para o snapshot
/// fiscal em jsonb (ver plano P1 do modelo de dados), que acomoda essa
/// variação sem exigir migration a cada mudança de layout nacional.
///
/// vLiq, ao contrário, é monetariamente simples, estável desde a v1.01
/// do layout e sempre presente na resposta, independente de o emitente
/// ser Simples Nacional, Lucro Presumido ou Lucro Real.
/// </summary>
public static class NfseXmlValoresParser
{
    private const string Namespace = "http://www.sped.fazenda.gov.br/nfse";

    /// <summary>
    /// Retorna o valor líquido (vLiq) do XML informado, ou null se o nó
    /// não existir ou não puder ser convertido — nunca lança exceção por
    /// um XML com formato inesperado, para não derrubar a persistência
    /// da Nfse por causa de um campo auxiliar/não crítico.
    /// </summary>
    public static decimal? ExtrairValorLiquido(string xmlNfse)
    {
        if (string.IsNullOrWhiteSpace(xmlNfse))
            return null;

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xmlNfse);

            var nsManager = new XmlNamespaceManager(doc.NameTable);
            nsManager.AddNamespace("nfse", Namespace);

            var node = doc.SelectSingleNode("//nfse:NFSe/nfse:infNFSe/nfse:valores/nfse:vLiq", nsManager);

            if (node is null)
                return null;

            return decimal.TryParse(node.InnerText, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor)
                ? valor
                : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }
}
