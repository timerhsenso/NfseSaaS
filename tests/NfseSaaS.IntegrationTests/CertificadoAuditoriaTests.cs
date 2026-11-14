using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Certificates;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// SEG1 — enviar e trocar o certificado A1 fica na auditoria da conta, com
/// titular, validade e impressão digital (nunca o arquivo nem a senha), e a
/// troca diz qual certificado saiu.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class CertificadoAuditoriaTests
{
    private const string Senha = "senha-do-pfx-123";
    private readonly AplicacaoTesteFactory _factory;

    public CertificadoAuditoriaTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Enviar_e_trocar_o_certificado_ficam_na_auditoria_sem_a_senha()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var pasta = Path.Combine(Path.GetTempPath(), "seg1-cert-" + s);

        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Certificado " + s, Perfil = PerfilConta.Empresa };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        var empresa = new Empresa
        {
            Cnpj = "04747304000178", RazaoSocial = "Empresa " + s, InscricaoMunicipal = "1", CodigoMunicipio = "2919207", Uf = "BA",
            Cep = "42700000", Logradouro = "Rua", Numero = "1", Bairro = "Centro", Telefone = "71999999999", Email = $"cert{s[..8]}@teste.com",
            OpSimpNac = "3", RegApTribSN = "1", RegEspTrib = "0", TribIssqn = "1", CstPisCofins = "00", PercentualTotalTributosSimplesNacional = "6.00"
        };
        db.Empresas.Add(empresa);
        await db.SaveChangesAsync();

        try
        {
            var store = new CertificateFileStore(new EphemeralDataProtectionProvider(), Options.Create(new CertificateStorageOptions { BasePath = pasta }));
            var enviar = new EnviarCertificadoUseCase(db, store, sp.GetRequiredService<IAuditLogWriter>());

            var (primeiro, digitalPrimeiro) = CriarPfx("CN=Primeiro certificado");
            var (segundo, digitalSegundo) = CriarPfx("CN=Segundo certificado");
            await enviar.ExecutarAsync(empresa.Id, primeiro, Senha, CancellationToken.None);
            await enviar.ExecutarAsync(empresa.Id, segundo, Senha, CancellationToken.None);

            var registros = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.TenantId == conta.Id && a.Operacao == "EnviarCertificado")
                .OrderBy(a => a.DataHora)
                .ToListAsync();
            Assert.Equal(2, registros.Count);
            Assert.All(registros, r => Assert.Equal(empresa.Id, r.EntidadeId));
            Assert.All(registros, r => Assert.DoesNotContain(Senha, r.Dados));

            using var dadosPrimeiro = JsonDocument.Parse(registros[0].Dados!);
            Assert.Equal(digitalPrimeiro, dadosPrimeiro.RootElement.GetProperty("ImpressaoDigital").GetString());
            Assert.Equal(JsonValueKind.Null, dadosPrimeiro.RootElement.GetProperty("Substituiu").ValueKind);
            Assert.Contains("Primeiro certificado", dadosPrimeiro.RootElement.GetProperty("Titular").GetString());

            using var dadosSegundo = JsonDocument.Parse(registros[1].Dados!);
            Assert.Equal(digitalSegundo, dadosSegundo.RootElement.GetProperty("ImpressaoDigital").GetString());
            Assert.Equal(digitalPrimeiro, dadosSegundo.RootElement.GetProperty("Substituiu").GetString());

            Assert.Equal(CertificateFileStore.VersaoAtualArquivo, await store.VersaoArquivoAsync(empresa.Id, CancellationToken.None));
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
            if (Directory.Exists(pasta))
                Directory.Delete(pasta, recursive: true);
        }
    }

    private static (byte[] Pfx, string ImpressaoDigital) CriarPfx(string titular)
    {
        using var rsa = RSA.Create(2048);
        var pedido = new CertificateRequest(titular, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificado = pedido.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(30));
        return (certificado.Export(X509ContentType.Pfx, Senha), certificado.Thumbprint);
    }
}
