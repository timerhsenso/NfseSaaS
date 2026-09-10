namespace NfseSaaS.Infrastructure.Documents;

/// <summary>
/// Raiz de armazenamento dos documentos de Contrato — separada da raiz de
/// certificados (CertificateStorageOptions) de propósito: perfis de
/// permissão/retenção diferentes, e um documento de contrato nunca deve
/// ficar na mesma pasta que a chave privada de um certificado digital.
/// </summary>
public sealed class DocumentStorageOptions
{
    public const string SectionName = "DocumentStorage";

    /// <summary>Diretório raiz onde os documentos ficam, organizados em {TenantId}/{EmpresaId}/contratos/{ContratoId}/{DocumentoId}.{extensao}.</summary>
    public string BasePath { get; set; } = string.Empty;
}
