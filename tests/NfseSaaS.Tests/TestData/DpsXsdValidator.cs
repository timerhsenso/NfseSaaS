using System.Xml;
using System.Xml.Schema;

namespace NfseSaaS.Tests.TestData;

/// <summary>
/// Valida um XML de DPS contra os XSDs OFICIAIS da SEFIN versionados em
/// tests/NfseSaaS.Tests/Schemas. Compartilhado entre os testes do
/// DpsBuilder — qualquer elemento fora de ordem, vazio ou faltando aparece
/// aqui com a mesma mensagem que a SEFIN devolveria (E1235).
/// </summary>
internal static class DpsXsdValidator
{
    private const string Ns = "http://www.sped.fazenda.gov.br/nfse";

    /// <summary>Esquemas de Produção Restrita publicados em 20/10/2026 (NT 009, release 1).</summary>
    public static readonly string PastaProducaoRestrita20261020 =
        Path.Combine(AppContext.BaseDirectory, "Schemas", "ProducaoRestrita_20261020");

    public static List<string> Validar(string xml, string pastaXsd)
    {
        var erros = new List<string>();

        // xmldsig-core-schema.xsd (W3C) declara um DOCTYPE — sem
        // DtdProcessing.Parse o XmlSchemaSet recusa carregá-lo. É arquivo
        // local, versionado no repo; nada é buscado na rede.
        var leituraXsd = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = new XmlUrlResolver()
        };

        var schemas = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
        using (var reader = XmlReader.Create(Path.Combine(pastaXsd, "DPS_v1.01.xsd"), leituraXsd))
            schemas.Add(Ns, reader);
        schemas.Compile();

        var leituraXml = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = schemas
        };
        leituraXml.ValidationEventHandler += (_, e) =>
            erros.Add($"[{e.Severity}] linha {e.Exception?.LineNumber}: {e.Message}");

        using var xmlReader = XmlReader.Create(new StringReader(xml), leituraXml);
        while (xmlReader.Read()) { }

        return erros;
    }
}
