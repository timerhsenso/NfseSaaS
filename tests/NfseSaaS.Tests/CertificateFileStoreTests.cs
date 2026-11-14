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

    // ---------- SEG1: finalidades separadas para o .pfx e a senha ----------

    [Fact]
    public async Task Arquivo_novo_e_versao_2_e_a_senha_nao_abre_com_a_finalidade_do_pfx()
    {
        var provedor = new EphemeralDataProtectionProvider();
        var store = new CertificateFileStore(provedor, Options.Create(new CertificateStorageOptions { BasePath = _tempDir }));
        var empresaId = Guid.NewGuid();
        var pfxBytes = CriarPfxDeTeste(out var senha);

        await store.SalvarAsync(empresaId, pfxBytes, senha, CancellationToken.None);

        Assert.Equal(CertificateFileStore.VersaoAtualArquivo, await store.VersaoArquivoAsync(empresaId, CancellationToken.None));
        using var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_tempDir, $"{empresaId:N}.cert.json")));
        var senhaProtegida = json.RootElement.GetProperty("SenhaProtegida").GetString()!;
        Assert.ThrowsAny<CryptographicException>(() => provedor.CreateProtector("NfseSaaS.Certificados.v1").Unprotect(senhaProtegida));

        using var carregado = await store.CarregarAsync(empresaId, CancellationToken.None);
        Assert.True(carregado.HasPrivateKey);
    }

    [Fact]
    public async Task Arquivo_antigo_versao_1_continua_abrindo_e_regravar_passa_para_a_versao_2()
    {
        var provedor = new EphemeralDataProtectionProvider();
        var store = new CertificateFileStore(provedor, Options.Create(new CertificateStorageOptions { BasePath = _tempDir }));
        var empresaId = Guid.NewGuid();
        var pfxBytes = CriarPfxDeTeste(out var senha);

        // Formato de antes do SEG1: sem "Versao", senha com a finalidade do .pfx.
        var antigo = provedor.CreateProtector("NfseSaaS.Certificados.v1");
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, $"{empresaId:N}.cert.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            PfxProtegidoBase64 = Convert.ToBase64String(antigo.Protect(pfxBytes)),
            SenhaProtegida = antigo.Protect(senha)
        }));

        Assert.Equal(1, await store.VersaoArquivoAsync(empresaId, CancellationToken.None));
        using (var carregado = await store.CarregarAsync(empresaId, CancellationToken.None))
            Assert.True(carregado.HasPrivateKey);

        await store.ReprotegerAsync(empresaId, CancellationToken.None);

        Assert.Equal(CertificateFileStore.VersaoAtualArquivo, await store.VersaoArquivoAsync(empresaId, CancellationToken.None));
        using var depois = await store.CarregarAsync(empresaId, CancellationToken.None);
        Assert.True(depois.HasPrivateKey);
    }

    [Fact]
    public async Task Provedor_anota_qual_certificado_foi_usado_para_a_auditoria()
    {
        var store = CriarStore();
        var empresaId = Guid.NewGuid();
        var pfxBytes = CriarPfxDeTeste(out var senha);
        await store.SalvarAsync(empresaId, pfxBytes, senha, CancellationToken.None);

        var usados = new CertificadosUsadosNoEscopo();
        using var certificado = await new FileCertificateProvider(store, usados).ObterCertificadoAsync(empresaId, CancellationToken.None);

        Assert.Equal(certificado.Thumbprint, usados.Obter(empresaId)?.Thumbprint);
        Assert.Null(usados.Obter(Guid.NewGuid()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}