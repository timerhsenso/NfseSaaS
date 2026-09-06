using System.Security.Cryptography.X509Certificates;

namespace NfseSaaS.Nacional.Signing;

/// <summary>Assina digitalmente (XML-DSig, enveloped) o elemento infDPS identificado por <paramref name="infDpsId"/>.</summary>
public interface IDpsSigner
{
    string Assinar(string xmlDps, string infDpsId, X509Certificate2 certificado);
}
