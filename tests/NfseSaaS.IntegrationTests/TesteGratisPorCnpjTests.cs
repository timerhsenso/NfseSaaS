using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Cobranca;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// Um teste grátis por CNPJ, com o banco real: a segunda conta com o mesmo
/// CNPJ não ganha teste (nem depois de a primeira ser excluída); conta de
/// contador não perde nem registra.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class TesteGratisPorCnpjTests
{
    private readonly AplicacaoTesteFactory _factory;

    public TesteGratisPorCnpjTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private static CobrancaService Servico(IServiceProvider sp) => new(
        sp.GetRequiredService<AppDbContext>(),
        sp.GetRequiredService<ICurrentTenant>(),
        sp.GetRequiredService<ICurrentUser>(),
        sp.GetRequiredService<IGatewayCobranca>(),
        sp.GetRequiredService<IAuditLogWriter>(),
        sp.GetRequiredService<ICatalogoPlanos>(),
        Options.Create(new CobrancaOptions { Ativa = true }),
        NullLogger<CobrancaService>.Instance);

    private static async Task<Tenant> ContaComEmpresaAsync(AppDbContext db, string cnpj, PerfilConta perfil = PerfilConta.Empresa)
    {
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Teste CNPJ " + s, Perfil = perfil };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();

        db.TenantIdOverrideDeSistema = conta.Id;
        db.Empresas.Add(new Empresa { Cnpj = cnpj, RazaoSocial = "Empresa " + s });
        await db.SaveChangesAsync();
        db.TenantIdOverrideDeSistema = null;
        return conta;
    }

    private static async Task LimparAsync(AppDbContext db, string cnpj, params Guid[] contas)
    {
        db.ChangeTracker.Clear();
        await db.TestesGratisCnpj.Where(t => t.Cnpj == cnpj).ExecuteDeleteAsync();
        foreach (var id in contas)
        {
            await db.Empresas.IgnoreQueryFilters().Where(e => e.TenantId == id).ExecuteDeleteAsync();
            await db.Assinaturas.Where(a => a.TenantId == id).ExecuteDeleteAsync();
            await db.Tenants.Where(t => t.Id == id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Segunda_conta_com_o_mesmo_cnpj_nao_ganha_teste_nem_depois_de_excluir_a_primeira()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var cnpj = Guid.NewGuid().ToString("N")[..14];

        var primeira = await ContaComEmpresaAsync(db, cnpj);
        var segunda = await ContaComEmpresaAsync(db, cnpj);
        var contador = await ContaComEmpresaAsync(db, cnpj, PerfilConta.Contador);
        var servico = Servico(sp);

        try
        {
            // Primeira nota em produção da primeira conta: teste começa e o CNPJ fica registrado.
            await servico.GarantirPodeEmitirEmProducaoAsync(primeira.Id, CancellationToken.None);
            var registro = await db.TestesGratisCnpj.AsNoTracking().SingleAsync(t => t.Cnpj == cnpj);
            Assert.Equal(primeira.Id, registro.TenantId);
            // A mesma conta continua emitindo (o CNPJ é dela).
            await servico.GarantirPodeEmitirEmProducaoAsync(primeira.Id, CancellationToken.None);

            // Contador com o mesmo CNPJ de cliente: tem teste normal.
            await servico.GarantirPodeEmitirEmProducaoAsync(contador.Id, CancellationToken.None);
            Assert.False((await db.Assinaturas.AsNoTracking().SingleAsync(a => a.TenantId == contador.Id)).SemTesteGratis);

            // A conta inteira some (a linha do registro fica: não tem FK).
            db.ChangeTracker.Clear();
            await db.Empresas.IgnoreQueryFilters().Where(e => e.TenantId == primeira.Id).ExecuteDeleteAsync();
            await db.Assinaturas.Where(a => a.TenantId == primeira.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(t => t.Id == primeira.Id).ExecuteDeleteAsync();
            Assert.True(await db.TestesGratisCnpj.AnyAsync(t => t.Cnpj == cnpj));

            // Segunda conta, mesmo CNPJ: bloqueia já na primeira nota em produção.
            var erro = await Assert.ThrowsAsync<RegraNegocioException>(() =>
                servico.GarantirPodeEmitirEmProducaoAsync(segunda.Id, CancellationToken.None));
            Assert.Contains("já usou o teste grátis em outra conta", erro.Message);

            db.ChangeTracker.Clear();
            var assinatura = await db.Assinaturas.AsNoTracking().SingleAsync(a => a.TenantId == segunda.Id);
            Assert.True(assinatura.SemTesteGratis);
            Assert.NotNull(assinatura.TesteIniciadoEm);

            // A tela avisa e mostra a primeira cobrança para hoje.
            sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(segunda.Id);
            IObterAssinaturaUseCase obter = servico;
            var tela = await obter.ExecutarAsync(CancellationToken.None);
            Assert.True(tela.SemTesteGratis);
            Assert.False(tela.PodeEmitirEmProducao);
            Assert.Equal(DataBrasilia.Hoje(), tela.PrimeiraCobrancaEm);
        }
        finally
        {
            await LimparAsync(db, cnpj, primeira.Id, segunda.Id, contador.Id);
        }
    }
}
