using System.Text;
using System.Xml;

namespace NfseSaaS.Nacional.Helpers;

/// <summary>
/// Serializa um XmlDocument para string UTF-8 sem BOM, preservando a
/// formatação usada nos documentos fiscais da NFS-e Nacional. Extraído do
/// que era o método FormatXml privado em NfsePoc/DpsBuilder.cs, agora
/// compartilhado entre DpsBuilder (XML sem assinatura) e DpsSigner (XML
/// já assinado) para não duplicar a lógica.
/// </summary>
public static class XmlSerializationHelper
{
    public static string ToXmlString(XmlDocument doc)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = false
        };

        using var sw = new Utf8StringWriter();
        using var writer = XmlWriter.Create(sw, settings);
        doc.Save(writer);
        return sw.ToString();
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}