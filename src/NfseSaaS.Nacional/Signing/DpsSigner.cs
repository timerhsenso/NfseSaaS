using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using NfseSaaS.Nacional.Exceptions;
using NfseSaaS.Nacional.Helpers;

namespace NfseSaaS.Nacional.Signing;

/// <summary>
/// Assinatura digital XML-DSig (RSA-SHA1 + C14N, enveloped signature) do
/// elemento infDPS. Migrado de NfsePoc/DpsBuilder.cs (método
/// AssinarInfDps) — mesmo algoritmo já validado com emissão real aceita
/// pela SEFIN (HTTP 201). RSA-SHA1/SHA1 são exigências do próprio schema
/// oficial da NFS-e Nacional, não escolha livre desta implementação.
/// </summary>
public sealed class DpsSigner : IDpsSigner
{
    public string Assinar(string xmlDps, string infDpsId, X509Certificate2 certificado)
    {
        using var rsa = certificado.GetRSAPrivateKey();
        if (rsa == null)
            throw new NfseCertificateException("Certificado não possui chave RSA privada.");

        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xmlDps);

        var signedXml = new SignedXml(doc)
        {
            SigningKey = rsa
        };

        signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
        signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;

        var reference = new Reference
        {
            Uri = "#" + infDpsId,
            DigestMethod = SignedXml.XmlDsigSHA1Url
        };

        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());
        signedXml.AddReference(reference);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(certificado));
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();

        var assinatura = signedXml.GetXml();
        var root = doc.DocumentElement ?? throw new NfseCertificateException("Documento XML da DPS sem elemento raiz.");
        root.AppendChild(doc.ImportNode(assinatura, true));

        return XmlSerializationHelper.ToXmlString(doc);
    }
}