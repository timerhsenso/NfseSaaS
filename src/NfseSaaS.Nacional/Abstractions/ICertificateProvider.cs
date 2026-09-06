using System.Security.Cryptography.X509Certificates;

namespace NfseSaaS.Nacional.Abstractions;

/// <summary>
/// Abstração para obtenção do certificado digital A1 usado no mTLS e na
/// assinatura da DPS. O módulo NfseSaaS.Nacional não sabe (nem deve saber)
/// se o certificado vem de arquivo local, Azure Key Vault, AWS Secrets
/// Manager ou de um banco criptografado — isso é decisão da implementação
/// concreta, registrada em NfseSaaS.Infrastructure (ou equivalente) na
/// fase em que a origem real do certificado for definida.
/// </summary>
public interface ICertificateProvider
{
    /// <summary>Obtém o certificado (com chave privada) da Empresa informada.</summary>
    Task<X509Certificate2> ObterCertificadoAsync(Guid empresaId, CancellationToken cancellationToken);
}
