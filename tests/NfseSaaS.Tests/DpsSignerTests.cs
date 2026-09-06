using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using NfseSaaS.Nacional.Builders;
using NfseSaaS.Nacional.Options;
using NfseSaaS.Nacional.Signing;
using NfseSaaS.Tests.TestData;
using Xunit;

namespace NfseSaaS.Tests;

public class DpsSignerTests
{
    /// <summary>
    /// Gera um certificado autoassinado só para teste (sem depender de
    /// nenhum arquivo .pfx externo) — equivalente, para fins de assinatura
    /// XML-DSig, ao certificado A1 real usado na POC.
    /// </summary>
    private static X509Certificate2 CriarCertificadoDeTeste()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Certificado de Teste",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var certificado = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));

        return new X509Certificate2(
            certificado.Export(X509ContentType.Pfx),
            (string?)null,
            X509KeyStorageFlags.Exportable);
    }

    [Fact]
    public void Assinar_deve_incluir_um_elemento_Signature_referenciando_o_infDpsId()
    {
        var options = Options.Create(new NfseNacionalOptions { Ambiente = "ProducaoRestrita" });
        var builder = new DpsBuilder(options);
        var signer = new DpsSigner();

        var request = DpsRequestFactory.CriarRequestDeTeste();
        var (xmlDps, infDpsId) = builder.Construir(request);

        using var certificado = CriarCertificadoDeTeste();
        var xmlAssinado = signer.Assinar(xmlDps, infDpsId, certificado);

        Assert.Contains("<Signature", xmlAssinado);
        Assert.Contains($"URI=\"#{infDpsId}\"", xmlAssinado);
    }

    [Fact]
    public void Assinar_sem_chave_privada_deve_lancar_NfseCertificateException()
    {
        var options = Options.Create(new NfseNacionalOptions { Ambiente = "ProducaoRestrita" });
        var builder = new DpsBuilder(options);
        var signer = new DpsSigner();

        var request = DpsRequestFactory.CriarRequestDeTeste();
        var (xmlDps, infDpsId) = builder.Construir(request);

        using var certificadoSemChavePrivada = new X509Certificate2(CriarCertificadoDeTeste().Export(X509ContentType.Cert));

        Assert.Throws<NfseSaaS.Nacional.Exceptions.NfseCertificateException>(
            () => signer.Assinar(xmlDps, infDpsId, certificadoSemChavePrivada));
    }
}