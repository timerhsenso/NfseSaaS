using System.Security.Cryptography.X509Certificates;

namespace NfseSaaS.Nacional.Signing;

/// <summary>
/// PLACEHOLDER — Fase 1.
///
/// A assinatura XML-DSig (RSA-SHA1 + C14N, enveloped signature) já foi
/// validada na POC (DpsBuilder.cs, método AssinarInfDps) com emissão real
/// aceita pela SEFIN (HTTP 201). Será migrada para cá na Fase 2.
/// </summary>
public sealed class DpsSigner : IDpsSigner
{
    public string Assinar(string xmlDps, string infDpsId, X509Certificate2 certificado)
    {
        throw new NotImplementedException(
            "Migrar de NfsePoc/DpsBuilder.cs (método AssinarInfDps) na Fase 2.");
    }
}
