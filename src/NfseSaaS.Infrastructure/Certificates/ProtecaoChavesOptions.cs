namespace NfseSaaS.Infrastructure.Certificates;

/// <summary>
/// Onde e como ficam as chaves do ASP.NET Core Data Protection — seção
/// "DataProtection" do appsettings (ou variáveis de ambiente
/// DataProtection__CertificadoPfxPath etc.).
///
/// Essas chaves descriptografam os certificados A1 de TODOS os clientes e
/// assinam o cookie de login. Perder as chaves = perder todos os
/// certificados; vazar as chaves (sem criptografia) = entregar todos os
/// certificados e permitir forjar login de qualquer tenant.
///
/// Todos os campos são opcionais e o padrão mantém o comportamento
/// anterior (chaves em {CertificateStorage:BasePath}/dp-keys), então
/// atualizar o sistema sem configurar nada não muda nem perde nada.
/// </summary>
public sealed class ProtecaoChavesOptions
{
    public const string SectionName = "DataProtection";

    /// <summary>
    /// Pasta das chaves. Vazio = {CertificateStorage:BasePath}/dp-keys
    /// (padrão histórico); se BasePath também estiver vazio, a pasta padrão
    /// do ASP.NET (Linux: ~/.aspnet/DataProtection-Keys). Ao mudar, COPIE os arquivos .xml da pasta antiga
    /// pra nova ANTES de subir — senão os certificados já enviados não
    /// abrem mais.
    /// </summary>
    public string KeysPath { get; set; } = string.Empty;

    /// <summary>
    /// .pfx usado pra criptografar as chaves gravadas em disco (fora do
    /// Windows; no Windows o DPAPI de máquina já faz isso). Deve ficar
    /// FORA da pasta das chaves e dos certificados dos clientes, e fora do
    /// backup comum dessas pastas. Vazio = chaves gravadas sem criptografia
    /// (comportamento anterior — o sistema registra um aviso na subida).
    /// </summary>
    public string CertificadoPfxPath { get; set; } = string.Empty;

    /// <summary>Senha do .pfx acima — use variável de ambiente, nunca appsettings versionado.</summary>
    public string CertificadoSenha { get; set; } = string.Empty;
}

/// <summary>Como as chaves ficaram configuradas nesta subida — lido pelo Program.cs pra avisar no log.</summary>
public sealed record ProtecaoChavesStatus(string CaminhoChaves, bool ChavesCriptografadas, string MecanismoCriptografia);
