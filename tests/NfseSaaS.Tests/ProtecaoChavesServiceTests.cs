using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Infrastructure.Certificates;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>
/// "Proteger chaves antigas" com o Data Protection real em disco: chave em
/// texto aberto (antes do certificado de proteção) → liga o certificado →
/// reprotege → a chave antiga some e o certificado da Empresa continua
/// abrindo, inclusive numa instância nova do sistema (como depois de
/// reiniciar o container).
/// </summary>
public sealed class ProtecaoChavesServiceTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "nfsesaas-protecao-chaves-" + Guid.NewGuid().ToString("N"));
    private string PastaChaves => Path.Combine(_raiz, "dp-keys");

    private readonly X509Certificate2 _certificadoProtecao = CriarCertificadoProtecao();

    private ServiceProvider CriarSistema(bool comCertificado)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddDataProtection()
            .SetApplicationName("NfseSaaS")
            .PersistKeysToFileSystem(new DirectoryInfo(PastaChaves));

        if (comCertificado)
            builder.ProtectKeysWithCertificate(_certificadoProtecao).UnprotectKeysWithAnyCertificate(_certificadoProtecao);

        return services.BuildServiceProvider();
    }

    private CertificateFileStore CriarStore(ServiceProvider sistema) =>
        new(sistema.GetRequiredService<IDataProtectionProvider>(), Options.Create(new CertificateStorageOptions { BasePath = _raiz }));

    private ProtecaoChavesService CriarServico(ServiceProvider sistema, bool comCertificado) =>
        new(new ProtecaoChavesStatus(PastaChaves, comCertificado, comCertificado ? "certificado teste" : "nenhum"),
            sistema.GetRequiredService<IKeyManager>(),
            CriarStore(sistema),
            NullLogger<ProtecaoChavesService>.Instance);

    [Fact]
    public async Task Reproteger_apaga_a_chave_antiga_e_o_certificado_continua_abrindo_depois_de_reiniciar()
    {
        var empresaId = Guid.NewGuid();
        var pfx = CriarPfxCliente("senha-do-cliente");

        // Antes: sistema sem certificado de proteção grava o certificado da Empresa.
        Guid chaveAntiga;
        using (var antes = CriarSistema(comCertificado: false))
        {
            await CriarStore(antes).SalvarAsync(empresaId, pfx, "senha-do-cliente", CancellationToken.None);
            chaveAntiga = Assert.Single(await CriarStore(antes).ChavesUsadasAsync(empresaId, CancellationToken.None));
        }

        Assert.Contains("masterKey", File.ReadAllText(Path.Combine(PastaChaves, $"key-{chaveAntiga}.xml")));

        // Depois: certificado de proteção configurado.
        using (var depois = CriarSistema(comCertificado: true))
        {
            var servico = CriarServico(depois, comCertificado: true);

            var situacao = servico.Consultar();
            Assert.Equal(1, situacao.ChavesSemCriptografia);
            Assert.Equal(1, situacao.Certificados);
            Assert.True(situacao.PodeReproteger);

            var resultado = await servico.ReprotegerAsync(Guid.NewGuid(), CancellationToken.None);

            Assert.Equal(1, resultado.CertificadosReprotegidos);
            Assert.Equal(1, resultado.ChavesRemovidas);
            Assert.NotEqual(chaveAntiga, resultado.ChaveNova);
            Assert.False(File.Exists(Path.Combine(PastaChaves, $"key-{chaveAntiga}.xml")));
            Assert.Contains("encryptedSecret", File.ReadAllText(Path.Combine(PastaChaves, $"key-{resultado.ChaveNova}.xml")));
            Assert.Equal(new[] { resultado.ChaveNova }, await CriarStore(depois).ChavesUsadasAsync(empresaId, CancellationToken.None));

            var situacaoFinal = servico.Consultar();
            Assert.Equal(0, situacaoFinal.ChavesSemCriptografia);
            Assert.False(situacaoFinal.PodeReproteger);
        }

        // "Reinício": instância nova, só com o que ficou em disco.
        using var reiniciado = CriarSistema(comCertificado: true);
        using var carregado = await CriarStore(reiniciado).CarregarAsync(empresaId, CancellationToken.None, validarPeriodoDeValidade: false);
        Assert.True(carregado.HasPrivateKey);
        Assert.DoesNotContain(Directory.EnumerateFiles(PastaChaves, "key-*.xml"), f => File.ReadAllText(f).Contains("<masterKey"));
    }

    [Fact]
    public async Task Reproteger_sem_certificado_de_protecao_recusa_e_nao_apaga_nada()
    {
        var empresaId = Guid.NewGuid();
        using var sistema = CriarSistema(comCertificado: false);
        await CriarStore(sistema).SalvarAsync(empresaId, CriarPfxCliente("x"), "x", CancellationToken.None);
        var antes = Directory.GetFiles(PastaChaves, "key-*.xml");

        var servico = CriarServico(sistema, comCertificado: false);
        Assert.False(servico.Consultar().PodeReproteger);
        await Assert.ThrowsAsync<RegraNegocioException>(() => servico.ReprotegerAsync(null, CancellationToken.None));

        Assert.Equal(antes, Directory.GetFiles(PastaChaves, "key-*.xml"));
    }

    [Fact]
    public void IdDaChave_recusa_conteudo_sem_o_cabecalho_do_Data_Protection()
    {
        Assert.ThrowsAny<Exception>(() => CertificateFileStore.IdDaChave(new byte[32]));
    }

    private static X509Certificate2 CriarCertificadoProtecao()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=NfseSaaS DataProtection (teste)", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificado = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return new X509Certificate2(certificado.Export(X509ContentType.Pfx, "p"), "p", X509KeyStorageFlags.Exportable);
    }

    private static byte[] CriarPfxCliente(string senha)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Cliente de Teste", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificado = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
        return certificado.Export(X509ContentType.Pfx, senha);
    }

    public void Dispose()
    {
        _certificadoProtecao.Dispose();
        if (Directory.Exists(_raiz))
            Directory.Delete(_raiz, recursive: true);
    }
}
