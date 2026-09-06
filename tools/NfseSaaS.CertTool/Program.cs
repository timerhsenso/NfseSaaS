// ==========================================================================
// NfseSaaS.CertTool
// --------------------------------------------------------------------------
// Ferramenta de linha de comando para cadastrar (ou atualizar) o
// certificado digital A1 (.pfx) de uma Empresa no armazenamento
// criptografado usado pelo NfseSaaS (CertificateFileStore, em
// NfseSaaS.Infrastructure).
//
// Uso:
//   NfseSaaS.CertTool <empresaId-guid> <caminho-do-pfx> <senha-do-pfx> <basePathCertificados>
//
// Exemplo:
//   NfseSaaS.CertTool 3fa85f64-5717-4562-b3fc-2c963f66afa6 C:\certs\empresa.pfx "MinhaSenh@123" C:\NfseSaaS-Secrets\certificados
//
// Usa a MESMA convenção de chaves de proteção (Data Protection + DPAPI de
// máquina) configurada em NfseSaaS.Infrastructure.DependencyInjection —
// por isso o "basePathCertificados" informado aqui deve ser exatamente o
// mesmo configurado em CertificateStorage:BasePath no appsettings do Web,
// senão a aplicação não vai conseguir descriptografar o que esta
// ferramenta gravar (chaves de proteção diferentes).
// ==========================================================================

using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using NfseSaaS.Infrastructure.Certificates;

if (args.Length < 4)
{
    Console.WriteLine("Uso: NfseSaaS.CertTool <empresaId-guid> <caminho-do-pfx> <senha-do-pfx> <basePathCertificados>");
    return 1;
}

if (!Guid.TryParse(args[0], out var empresaId))
{
    Console.WriteLine($"ERRO: '{args[0]}' não é um GUID válido de empresaId.");
    return 1;
}

var caminhoPfx = args[1];
var senha = args[2];
var basePath = args[3];

if (!File.Exists(caminhoPfx))
{
    Console.WriteLine($"ERRO: arquivo não encontrado: {caminhoPfx}");
    return 1;
}

var pfxBytes = await File.ReadAllBytesAsync(caminhoPfx);

Console.WriteLine("Validando o certificado antes de gravar...");

try
{
    using var teste = new X509Certificate2(pfxBytes, senha, X509KeyStorageFlags.EphemeralKeySet);

    Console.WriteLine($"  Subject   : {teste.Subject}");
    Console.WriteLine($"  Validade  : {teste.NotBefore:dd/MM/yyyy} até {teste.NotAfter:dd/MM/yyyy}");
    Console.WriteLine($"  Priv. Key : {teste.HasPrivateKey}");

    if (!teste.HasPrivateKey)
    {
        Console.WriteLine("ERRO: o certificado não possui chave privada.");
        return 1;
    }

    if (DateTime.Now < teste.NotBefore || DateTime.Now > teste.NotAfter)
    {
        Console.WriteLine("AVISO: o certificado está fora do período de validade — gravando mesmo assim, mas a emissão vai falhar.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"ERRO ao abrir o .pfx (senha incorreta ou arquivo inválido): {ex.Message}");
    return 1;
}

var keysPath = Path.Combine(basePath, "dp-keys");
Directory.CreateDirectory(keysPath);

var dataProtectionProvider = DataProtectionProvider.Create(
    new DirectoryInfo(keysPath),
    builder =>
    {
        builder.SetApplicationName("NfseSaaS");
        if (OperatingSystem.IsWindows())
            builder.ProtectKeysWithDpapi(protectToLocalMachine: true);
    });

var store = new CertificateFileStore(
    dataProtectionProvider,
    Options.Create(new CertificateStorageOptions { BasePath = basePath }));

await store.SalvarAsync(empresaId, pfxBytes, senha, CancellationToken.None);

Console.WriteLine();
Console.WriteLine($"Certificado da empresa {empresaId} salvo com sucesso em:");
Console.WriteLine($"  {Path.Combine(basePath, $"{empresaId:N}.cert.json")}");

return 0;