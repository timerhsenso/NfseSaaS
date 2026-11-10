using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Application.UseCases.Municipios;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>C9 com o banco real: tabela carregada no startup, busca pelo nome e conferência do código ao salvar.</summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class MunicipiosTests
{
    private readonly AplicacaoTesteFactory _factory;

    public MunicipiosTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Busca_pelo_nome_sem_acento_com_uf_e_pelo_codigo()
    {
        using var escopo = _factory.Services.CreateScope();
        var municipios = escopo.ServiceProvider.GetRequiredService<IMunicipiosUseCase>();

        Assert.True(await escopo.ServiceProvider.GetRequiredService<AppDbContext>().Municipios.CountAsync() >= 5570);

        var salvador = await municipios.BuscarAsync("salvador", null, 20, CancellationToken.None);
        Assert.Equal("2927408", salvador[0].Codigo);

        var sp = await municipios.BuscarAsync("sao paulo/sp", null, 20, CancellationToken.None);
        Assert.Equal("3550308", sp[0].Codigo);
        Assert.All(sp, m => Assert.Equal("SP", m.Uf));

        var lauro = Assert.Single(await municipios.BuscarAsync("2919207", null, 20, CancellationToken.None));
        Assert.Equal("Lauro de Freitas", lauro.Nome);

        Assert.Empty(await municipios.BuscarAsync("s", null, 20, CancellationToken.None));
    }

    [Fact]
    public async Task Codigo_inexistente_e_recusado_e_a_uf_vem_do_municipio()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Mun " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        try
        {
            var empresa = new Empresa { Cnpj = s[..14], RazaoSocial = "Empresa " + s };
            db.Empresas.Add(empresa);
            await db.SaveChangesAsync();

            var cadastrar = new CadastrarClienteUseCase(db, sp.GetRequiredService<IAuditLogWriter>());
            CadastrarClienteRequest Pedido(string municipio, string uf) => new(
                empresa.Id, "11222333000181", "Cliente", "Ap " + s[..6], null, null, municipio, "40010000", "Rua A", "1", null, "Centro", uf);

            var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => cadastrar.ExecutarAsync(Pedido("2999999", "BA"), CancellationToken.None));
            Assert.Equal(RegrasMunicipio.MensagemNaoExiste("2999999"), erro.Message);

            // UF digitada errada: vale a do município.
            var id = await cadastrar.ExecutarAsync(Pedido("2927408", "SP"), CancellationToken.None);
            Assert.Equal("BA", await db.Clientes.AsNoTracking().Where(c => c.Id == id).Select(c => c.Uf).SingleAsync());
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Clientes.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }
}
