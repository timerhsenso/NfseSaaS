using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace NfseSaaS.Infrastructure.Certificates;

/// <summary>
/// CNPJ do titular de um certificado e-CNPJ (ICP-Brasil). Fonte oficial: o
/// "otherName" 2.16.76.1.3.3 do Subject Alternative Name (DOC-ICP-04). Se
/// não houver, cai no padrão "RAZAO SOCIAL:12345678000190" do CN, usado
/// pelas certificadoras. Devolve nulo quando não acha (ex.: e-CPF): quem
/// chama decide o que fazer.
/// </summary>
public static partial class CnpjDoCertificado
{
    private const string OidSubjectAltName = "2.5.29.17";
    private const string OidCnpjIcpBrasil = "2.16.76.1.3.3";

    public static string? Extrair(X509Certificate2 certificado)
    {
        var doSan = certificado.Extensions
            .Where(e => e.Oid?.Value == OidSubjectAltName)
            .Select(e => LerCnpjDoSan(e.RawData))
            .FirstOrDefault(c => c is not null);
        if (doSan is not null)
            return doSan;

        var cn = certificado.GetNameInfo(X509NameType.SimpleName, forIssuer: false) ?? string.Empty;
        var achado = CnpjNoNome().Match(cn);
        return achado.Success ? achado.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>
    /// A empresa pode usar o certificado do mesmo CNPJ ou da matriz/filial
    /// (mesma raiz, os 8 primeiros caracteres).
    /// </summary>
    public static bool MesmaRaiz(string cnpjCertificado, string cnpjEmpresa) =>
        cnpjCertificado.Length >= 8 && cnpjEmpresa.Length >= 8
        && string.Equals(cnpjCertificado[..8], cnpjEmpresa[..8], StringComparison.OrdinalIgnoreCase);

    private static string? LerCnpjDoSan(byte[] raw)
    {
        try
        {
            var nomes = new AsnReader(raw, AsnEncodingRules.DER).ReadSequence();
            while (nomes.HasData)
            {
                var tag = nomes.PeekTag();
                // GeneralName otherName: [0] IMPLICIT SEQUENCE { type-id OID, value [0] EXPLICIT ANY }
                if (tag.TagClass == TagClass.ContextSpecific && tag.TagValue == 0 && tag.IsConstructed)
                {
                    var outro = nomes.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
                    var oid = outro.ReadObjectIdentifier();
                    var valor = outro.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
                    if (oid == OidCnpjIcpBrasil)
                    {
                        var texto = LerTexto(valor);
                        var cnpj = new string(texto.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
                        return cnpj.Length == 14 ? cnpj : null;
                    }
                }
                else
                {
                    nomes.ReadEncodedValue();
                }
            }
        }
        catch (AsnContentException)
        {
            // Extensão fora do padrão: tenta pelo nome.
        }
        return null;
    }

    /// <summary>O valor vem como OCTET STRING ou como string ASN.1 (Printable/UTF8/IA5), conforme a certificadora.</summary>
    private static string LerTexto(AsnReader valor)
    {
        var tag = valor.PeekTag();
        if (tag.HasSameClassAndValue(Asn1Tag.PrimitiveOctetString))
            return Encoding.ASCII.GetString(valor.ReadOctetString());
        return valor.ReadCharacterString((UniversalTagNumber)tag.TagValue);
    }

    [GeneratedRegex(@":([0-9A-Za-z]{12}[0-9]{2})\s*$")]
    private static partial Regex CnpjNoNome();
}
