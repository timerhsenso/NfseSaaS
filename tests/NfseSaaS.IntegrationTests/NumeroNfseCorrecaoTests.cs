using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.Persistence.Seeders;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// Correção ao subir o sistema: nota que guardou o Id ("NFS" + chave ou
/// "DPS" + …) no número passa a mostrar o número oficial curto.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class NumeroNfseCorrecaoTests
{
    private readonly AplicacaoTesteFactory _factory;

    public NumeroNfseCorrecaoTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private static string Chave(int numero) =>
        "2919207" + "2" + "2" + "04747304000178" + numero.ToString("D13") + "2611" + "083642406" + "0";

    [Fact]
    public async Task Troca_o_Id_gravado_pelo_numero_oficial()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Numero " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        try
        {
            var empresa = new Empresa { Cnpj = s[..14], RazaoSocial = "Empresa " + s };
            db.Empresas.Add(empresa);
            await db.SaveChangesAsync();
            var cliente = new Cliente { EmpresaId = empresa.Id, Nome = "Cliente", Apelido = "CLI" + s[..6], CpfCnpj = "52998224725" };
            db.Clientes.Add(cliente);
            await db.SaveChangesAsync();

            Nfse Nota(int dps, string? numero, string? chave, string? xml) => new()
            {
                EmpresaId = empresa.Id, ClienteId = cliente.Id, NumeroDps = dps, SerieDps = "1",
                NumeroNfse = numero, ChaveAcesso = chave, XmlNfse = xml,
                DataCompetencia = new DateOnly(2026, 11, 1), ValorServico = 10m,
                Status = NfseStatus.Autorizada, TipoAmbiente = TipoAmbiente.Producao
            };

            var xml44 = $"<NFSe xmlns=\"http://www.sped.fazenda.gov.br/nfse\"><infNFSe Id=\"NFS{Chave(44)}\"><nNFSe>44</nNFSe></infNFSe></NFSe>";
            var comXml = Nota(1, "NFS" + Chave(44), Chave(44), xml44);
            var soChave = Nota(2, "NFS" + Chave(43), Chave(43), null);
            var soValor = Nota(3, "NFS" + Chave(42), null, null);
            var idDps = Nota(4, "DPS291920720474730400017800001000000000465396", null, null);
            var certa = Nota(5, "41", Chave(41), null);
            db.NotasFiscais.AddRange(comXml, soChave, soValor, idDps, certa);
            await db.SaveChangesAsync();

            await NumeroNfseCorrecao.CorrigirAsync(_factory.Services);
            // Idempotente: rodar de novo não muda nada.
            await NumeroNfseCorrecao.CorrigirAsync(_factory.Services);

            db.ChangeTracker.Clear();
            var numeros = await db.NotasFiscais.IgnoreQueryFilters().AsNoTracking()
                .Where(n => n.TenantId == conta.Id)
                .ToDictionaryAsync(n => n.NumeroDps, n => n.NumeroNfse);
            Assert.Equal("44", numeros[1]);
            Assert.Equal("43", numeros[2]);
            Assert.Equal("42", numeros[3]);
            Assert.Null(numeros[4]);
            Assert.Equal("41", numeros[5]);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.NotasFiscais.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Clientes.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }
}
