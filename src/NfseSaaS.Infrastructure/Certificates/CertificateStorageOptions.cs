namespace NfseSaaS.Infrastructure.Certificates;

/// <summary>Configuração de onde os certificados .pfx criptografados de cada Empresa ficam armazenados.</summary>
public sealed class CertificateStorageOptions
{
    public const string SectionName = "CertificateStorage";

    /// <summary>
    /// Diretório onde os arquivos de certificado (já criptografados) ficam.
    /// DEVE estar fora do wwwroot e fora do controle de versão — configure
    /// permissões NTFS restritas (apenas a identidade do App Pool / administradores).
    /// </summary>
    public string BasePath { get; set; } = string.Empty;
}