using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using NfseSaaS.Infrastructure.Certificates;
using NfseSaaS.Nacional.Exceptions;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>
/// Testa o round-trip de criptografia/descriptografia do certificado sem
/// depender de DPAPI real (usa EphemeralDataProtectionProvider, puramente
/// em memória, adequado para testes e independente de sistema operacional).
/// </summary>
public class CertificateFileStoreTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "nfsesaas-cert-tests-" + Guid.NewGuid().ToString("N"));

    private CertificateFileStore CriarStore()
    {
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var options = Options.Create(new CertificateStorageOptions { BasePath = _tempDir });
        return new CertificateFileStore(dataProtectionProvider, options);
    }

    private static byte[] CriarPfxDeTeste(out string senha)
    {
        senha = "senha-teste-123";
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Certificado de Teste", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificado = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
        return certificado.Export(X509ContentType.Pfx, senha);
    }

    [Fact]
    public async Task SalvarECarregar_deve_recuperar_um_certificado_com_chave_privada_utilizavel()
    {
        var store = CriarStore();
        var empresaId = Guid.NewGuid();
        var pfxBytes = CriarPfxDeTeste(out var senha);

        await store.SalvarAsync(empresaId, pfxBytes, senha, CancellationToken.None);
        using var certificadoCarregado = await store.CarregarAsync(empresaId, CancellationToken.None);

        Assert.True(certificadoCarregado.HasPrivateKey);
        Assert.NotNull(certificadoCarregado.GetRSAPrivateKey());
    }

    [Fact]
    public async Task Carregar_para_empresa_sem_certificado_salvo_deve_lancar_NfseCertificateException()
    {
        var store = CriarStore();

        await Assert.ThrowsAsync<NfseCertificateException>(
            () => store.CarregarAsync(Guid.NewGuid(), CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}