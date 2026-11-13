using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Application.UseCases.Recursos;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// PL1 com o banco real: sem plano vale o padrão (nada muda), a grade do
/// plano vale para quem assina, a exceção vence o plano enquanto valer e o
/// limite de clientes barra só o cadastro novo.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class RecursosPlanoTests
{
    private readonly AplicacaoTesteFactory _factory;

    public RecursosPlanoTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed record Cenario(Tenant Conta, Plano Plano, Empresa Empresa);

    private static async Task<Cenario> CriarAsync(IServiceProvider sp, bool comPlano)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Recursos " + s, Perfil = PerfilConta.Empresa };
        var plano = new Plano { Codigo = 700000 + Random.Shared.Next(99999), Nome = "Rec " + s[..6], Descricao = "teste", MaxEmpresas = 1 };
        db.Tenants.Add(conta);
        db.Planos.Add(plano);
        db.Assinaturas.Add(new Assinatura { TenantId = conta.Id, Status = comPlano ? StatusAssinatura.Ativa : StatusAssinatura.Teste, Plano = comPlano ? plano.Codigo : null });
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        var empresa = new Empresa
        {
            Cnpj = "04747304000178", RazaoSocial = "Empresa " + s, InscricaoMunicipal = "1", CodigoMunicipio = "2919207", Uf = "BA",
            Cep = "42700000", Logradouro = "Rua", Numero = "1", Bairro = "Centro", Telefone = "71999999999", Email = "e@teste.com",
            OpSimpNac = "3", RegApTribSN = "1", RegEspTrib = "0", TribIssqn = "1", CstPisCofins = "00", PercentualTotalTributosSimplesNacional = "6.00"
        };
        db.Empresas.Add(empresa);
        await db.SaveChangesAsync();
        return new Cenario(conta, plano, empresa);
    }

    private static async Task LimparAsync(IServiceProvider sp, Cenario c)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        db.ChangeTracker.Clear();
        await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == c.Conta.Id).ExecuteDeleteAsync();
        await db.Clientes.IgnoreQueryFilters().Where(x => x.TenantId == c.Conta.Id).ExecuteDeleteAsync();
        await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == c.Conta.Id).ExecuteDeleteAsync();
        await db.ExcecoesRecursoConta.Where(x => x.TenantId == c.Conta.Id).ExecuteDeleteAsync();
        await db.Assinaturas.Where(x => x.TenantId == c.Conta.Id).ExecuteDeleteAsync();
        await db.PlanosRecursos.Where(x => x.PlanoId == c.Plano.Id).ExecuteDeleteAsync();
        await db.Planos.Where(x => x.Id == c.Plano.Id).ExecuteDeleteAsync();
        await db.Tenants.Where(x => x.Id == c.Conta.Id).ExecuteDeleteAsync();
    }

    private static CadastrarClienteRequest Cliente(Guid empresaId, string doc, string apelido) =>
        new(empresaId, doc, "Cliente " + apelido, apelido, null, null, "2919207", "42700000", "Rua", "1", null, "Centro", "BA", Compartilhado: false);

    [Fact]
    public async Task Sem_plano_vale_o_padrao_e_a_grade_do_plano_vale_para_quem_assina()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var semPlano = await CriarAsync(sp, comPlano: false);
        try
        {
            var recursos = sp.GetRequiredService<IRecursosConta>();
            var admin = sp.GetRequiredService<IAdministrarRecursosUseCase>();

            // Conta Empresa sem plano: logo fechada (como antes do PL1).
            Assert.False(await recursos.LigadoAsync(semPlano.Conta.Id, CatalogoRecursos.LogoPropria, CancellationToken.None));

            // Grade: plano libera a logo — conta SEM plano continua no padrão.
            await admin.SalvarRecursosDoPlanoAsync(semPlano.Plano.Id, new SalvarRecursosPlanoRequest(new[]
            {
                new ValorRecursoPlanoRequest(CatalogoRecursos.LogoPropria, false, true, null),
                new ValorRecursoPlanoRequest(CatalogoRecursos.ClientesPorEmpresa, false, null, 1)
            }), null, CancellationToken.None);
            Assert.False(await recursos.LigadoAsync(semPlano.Conta.Id, CatalogoRecursos.LogoPropria, CancellationToken.None));

            // Passa a assinar o plano: vale a grade.
            var db = sp.GetRequiredService<AppDbContext>();
            await db.Assinaturas.Where(a => a.TenantId == semPlano.Conta.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.Plano, semPlano.Plano.Codigo));
            Assert.True(await recursos.LigadoAsync(semPlano.Conta.Id, CatalogoRecursos.LogoPropria, CancellationToken.None));
            var limite = await recursos.ObterAsync(semPlano.Conta.Id, CatalogoRecursos.ClientesPorEmpresa, CancellationToken.None);
            Assert.Equal(1, limite.Limite);
            Assert.Equal(OrigemRecurso.Plano, limite.Origem);

            // "Padrão" tira a linha do plano.
            var grade = await admin.SalvarRecursosDoPlanoAsync(semPlano.Plano.Id, new SalvarRecursosPlanoRequest(new[]
            {
                new ValorRecursoPlanoRequest(CatalogoRecursos.LogoPropria, true, null, null)
            }), null, CancellationToken.None);
            Assert.False(grade.Itens.Single(i => i.Codigo == CatalogoRecursos.LogoPropria).Configurado);
            Assert.True(grade.Itens.Single(i => i.Codigo == CatalogoRecursos.ClientesPorEmpresa).Configurado);
            Assert.False(await recursos.LigadoAsync(semPlano.Conta.Id, CatalogoRecursos.LogoPropria, CancellationToken.None));
            Assert.True(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.TenantId == semPlano.Conta.Id && a.Operacao == "AlterarRecursosPlano"));
        }
        finally
        {
            await LimparAsync(sp, semPlano);
        }
    }

    [Fact]
    public async Task Limite_de_clientes_barra_so_o_cadastro_e_a_excecao_libera()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var c = await CriarAsync(sp, comPlano: true);
        try
        {
            var admin = sp.GetRequiredService<IAdministrarRecursosUseCase>();
            var cadastrar = sp.GetRequiredService<ICadastrarClienteUseCase>();
            await admin.SalvarRecursosDoPlanoAsync(c.Plano.Id, new SalvarRecursosPlanoRequest(new[]
            {
                new ValorRecursoPlanoRequest(CatalogoRecursos.ClientesPorEmpresa, false, null, 1)
            }), null, CancellationToken.None);

            await cadastrar.ExecutarAsync(Cliente(c.Empresa.Id, "52998224725", "Primeiro"), CancellationToken.None);
            var barrado = await Assert.ThrowsAsync<RegraNegocioException>(() =>
                cadastrar.ExecutarAsync(Cliente(c.Empresa.Id, "11144477735", "Segundo"), CancellationToken.None));
            Assert.Contains("até 1 clientes por empresa", barrado.Message);

            // Exceção da conta: limite 5, com motivo e prazo.
            var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
            var conta = await admin.SalvarExcecaoAsync(c.Conta.Id,
                new SalvarExcecaoRecursoRequest(CatalogoRecursos.ClientesPorEmpresa, null, 5, "Negociação com o escritório", hoje.AddDays(30)), null, CancellationToken.None);
            var resolvido = conta.Recursos.Single(r => r.Codigo == CatalogoRecursos.ClientesPorEmpresa);
            Assert.Equal(5, resolvido.Limite);
            Assert.Equal(OrigemRecurso.Excecao, resolvido.Origem);
            await cadastrar.ExecutarAsync(Cliente(c.Empresa.Id, "11144477735", "Segundo"), CancellationToken.None);

            // Exceção vencida não vale; remover volta ao plano.
            var db = sp.GetRequiredService<AppDbContext>();
            await db.ExcecoesRecursoConta.Where(e => e.TenantId == c.Conta.Id).ExecuteUpdateAsync(u => u.SetProperty(e => e.ValidaAte, hoje.AddDays(-1)));
            var vencida = await admin.RecursosDaContaAsync(c.Conta.Id, CancellationToken.None);
            Assert.Equal(1, vencida.Recursos.Single(r => r.Codigo == CatalogoRecursos.ClientesPorEmpresa).Limite);
            Assert.True(vencida.Excecoes.Single().Vencida);

            var removida = await admin.RemoverExcecaoAsync(c.Conta.Id, vencida.Excecoes.Single().Id, null, CancellationToken.None);
            Assert.Empty(removida.Excecoes);
            Assert.True(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.TenantId == c.Conta.Id && a.Operacao == "RemoverExcecaoRecurso"));

            // Motivo curto e data passada são recusados.
            await Assert.ThrowsAsync<RegraNegocioException>(() => admin.SalvarExcecaoAsync(c.Conta.Id,
                new SalvarExcecaoRecursoRequest(CatalogoRecursos.LogoPropria, true, null, "curto", null), null, CancellationToken.None));
            await Assert.ThrowsAsync<RegraNegocioException>(() => admin.SalvarExcecaoAsync(c.Conta.Id,
                new SalvarExcecaoRecursoRequest(CatalogoRecursos.LogoPropria, true, null, "Cortesia de lançamento", hoje.AddDays(-1)), null, CancellationToken.None));
        }
        finally
        {
            await LimparAsync(sp, c);
        }
    }
}
