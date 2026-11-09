using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>C3 com o banco real: edição sem endereço aceita para CPF e recusada para CNPJ e para quem retém o ISS.</summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class EnderecoClienteTests
{
    private readonly AplicacaoTesteFactory _factory;

    public EnderecoClienteTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private static AtualizarClienteRequest SemEndereco(bool retem = false) =>
        new("Nome", "Apelido", null, null, null, null, null, null, null, null, null, RetemIss: retem);

    [Fact]
    public async Task Edicao_sem_endereco_so_para_pessoa_fisica_sem_retencao()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "End " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        try
        {
            var empresa = new Empresa { Cnpj = s[..14], RazaoSocial = "Empresa " + s };
            db.Empresas.Add(empresa);
            await db.SaveChangesAsync();
            Cliente Novo(string doc, string apelido) => new()
            {
                EmpresaId = empresa.Id, CpfCnpj = doc, Nome = apelido, Apelido = apelido,
                CodigoMunicipio = "2927408", Cep = "40010000", Logradouro = "Rua A", Numero = "1", Bairro = "Centro", Uf = "BA"
            };
            var pf = Novo("52998224725", "PF " + s[..6]);
            var pj = Novo("11222333000181", "PJ " + s[..6]);
            db.Clientes.AddRange(pf, pj);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var editar = new AtualizarClienteUseCase(db, sp.GetRequiredService<IAuditLogWriter>());

            await editar.ExecutarAsync(pf.Id, SemEndereco() with { Apelido = pf.Apelido }, CancellationToken.None);
            var salvo = await db.Clientes.AsNoTracking().SingleAsync(c => c.Id == pf.Id);
            Assert.Equal("", salvo.Cep);
            Assert.Equal("", salvo.Logradouro);
            Assert.Null(salvo.Complemento);

            var erroPj = await Assert.ThrowsAsync<RegraNegocioException>(() =>
                editar.ExecutarAsync(pj.Id, SemEndereco() with { Apelido = pj.Apelido }, CancellationToken.None));
            Assert.Equal(EnderecoCliente.MotivoCnpj, erroPj.Message);

            var erroRetem = await Assert.ThrowsAsync<RegraNegocioException>(() =>
                editar.ExecutarAsync(pf.Id, SemEndereco(retem: true) with { Apelido = pf.Apelido }, CancellationToken.None));
            Assert.Equal(EnderecoCliente.MotivoRetencao, erroRetem.Message);
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
