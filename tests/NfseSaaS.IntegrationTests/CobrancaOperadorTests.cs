using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Application.UseCases.Plataforma;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Cobranca;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// Ações de cobrança do operador (Plataforma › Contas) com o banco real:
/// prorrogar e liberar teste, isentar e remover, travas para conta com
/// assinatura no Asaas, Histórico na conta e números da lista de contas.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class CobrancaOperadorTests
{
    private readonly AplicacaoTesteFactory _factory;

    public CobrancaOperadorTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private static CobrancaService Cobranca(IServiceProvider sp) => new(
        sp.GetRequiredService<AppDbContext>(),
        sp.GetRequiredService<ICurrentTenant>(),
        sp.GetRequiredService<ICurrentUser>(),
        sp.GetRequiredService<IGatewayCobranca>(),
        sp.GetRequiredService<IAuditLogWriter>(),
        sp.GetRequiredService<ICatalogoPlanos>(),
        Options.Create(new CobrancaOptions { Ativa = true }),
        NullLogger<CobrancaService>.Instance);

    private static async Task<Tenant> ContaAsync(AppDbContext db, string cnpj)
    {
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Operador " + s };
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
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync();
            await db.Empresas.IgnoreQueryFilters().Where(e => e.TenantId == id).ExecuteDeleteAsync();
            await db.Assinaturas.Where(a => a.TenantId == id).ExecuteDeleteAsync();
            await db.Tenants.Where(t => t.Id == id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Prorrogar_e_liberar_teste_reabrem_a_emissao_e_ficam_no_historico()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var operador = sp.GetRequiredService<ICobrancaOperadorUseCase>();
        var cnpj = Guid.NewGuid().ToString("N")[..14];
        var primeira = await ContaAsync(db, cnpj);
        var segunda = await ContaAsync(db, cnpj);
        var cobranca = Cobranca(sp);

        try
        {
            // Teste da primeira conta acabou há 5 dias.
            await cobranca.GarantirPodeEmitirEmProducaoAsync(primeira.Id, CancellationToken.None);
            await db.Assinaturas.Where(a => a.TenantId == primeira.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.TesteIniciadoEm, DateTimeOffset.UtcNow.AddDays(-35)));
            db.ChangeTracker.Clear();
            await Assert.ThrowsAsync<RegraNegocioException>(() => cobranca.GarantirPodeEmitirEmProducaoAsync(primeira.Id, CancellationToken.None));

            // Prorrogar 3 dias não basta (ainda no passado); 10 dias reabre.
            await Assert.ThrowsAsync<RegraNegocioException>(() =>
                operador.ProrrogarTesteAsync(primeira.Id, new ProrrogarTesteRequest(3, "teste"), null, CancellationToken.None));
            var r = await operador.ProrrogarTesteAsync(primeira.Id, new ProrrogarTesteRequest(10, "contador parceiro"), null, CancellationToken.None);
            Assert.Contains("termina em", r.Mensagem);
            db.ChangeTracker.Clear();
            await cobranca.GarantirPodeEmitirEmProducaoAsync(primeira.Id, CancellationToken.None);
            Assert.True(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(l => l.TenantId == primeira.Id && l.Operacao == "ProrrogarTeste"));

            // Segunda conta, mesmo CNPJ: sem teste. Prorrogar não serve; liberar novo teste sim.
            await Assert.ThrowsAsync<RegraNegocioException>(() => cobranca.GarantirPodeEmitirEmProducaoAsync(segunda.Id, CancellationToken.None));
            db.ChangeTracker.Clear();
            await Assert.ThrowsAsync<RegraNegocioException>(() =>
                operador.ProrrogarTesteAsync(segunda.Id, new ProrrogarTesteRequest(10, "teste"), null, CancellationToken.None));
            await operador.LiberarTesteAsync(segunda.Id, new LiberarTesteRequest("CNPJ mudou de dono"), null, CancellationToken.None);
            db.ChangeTracker.Clear();
            await cobranca.GarantirPodeEmitirEmProducaoAsync(segunda.Id, CancellationToken.None);
            var assinatura = await db.Assinaturas.AsNoTracking().SingleAsync(a => a.TenantId == segunda.Id);
            Assert.False(assinatura.SemTesteGratis);
            Assert.Equal(segunda.Id, (await db.TestesGratisCnpj.AsNoTracking().SingleAsync(t => t.Cnpj == cnpj)).TenantId);
        }
        finally
        {
            await LimparAsync(db, cnpj, primeira.Id, segunda.Id);
        }
    }

    [Fact]
    public async Task Isentar_libera_a_emissao_e_aparece_na_lista_mas_nao_vale_para_quem_tem_assinatura_no_asaas()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var operador = sp.GetRequiredService<ICobrancaOperadorUseCase>();
        var cnpj = Guid.NewGuid().ToString("N")[..14];
        var conta = await ContaAsync(db, cnpj);
        var assinante = await ContaAsync(db, Guid.NewGuid().ToString("N")[..14]);
        var cobranca = Cobranca(sp);
        var hoje = DataBrasilia.Hoje();

        try
        {
            await cobranca.GarantirPodeEmitirEmProducaoAsync(conta.Id, CancellationToken.None);
            await db.Assinaturas.Where(a => a.TenantId == conta.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.TesteIniciadoEm, DateTimeOffset.UtcNow.AddDays(-40)));
            db.ChangeTracker.Clear();
            await Assert.ThrowsAsync<RegraNegocioException>(() => cobranca.GarantirPodeEmitirEmProducaoAsync(conta.Id, CancellationToken.None));

            await Assert.ThrowsAsync<RegraNegocioException>(() =>
                operador.IsentarAsync(conta.Id, new IsentarContaRequest(hoje.AddDays(-1), "passado"), null, CancellationToken.None));
            await operador.IsentarAsync(conta.Id, new IsentarContaRequest(hoje.AddMonths(3), "parceiro"), null, CancellationToken.None);
            db.ChangeTracker.Clear();
            await cobranca.GarantirPodeEmitirEmProducaoAsync(conta.Id, CancellationToken.None);

            var lista = await sp.GetRequiredService<IListarTenantsPlataformaUseCase>().ExecutarAsync(CancellationToken.None);
            var linha = lista.Single(t => t.Id == conta.Id).Cobranca!;
            Assert.True(linha.Isenta);
            Assert.False(linha.IsentaNaConfiguracao);
            Assert.Equal(hoje.AddMonths(3), linha.IsentaAte);
            Assert.Equal("parceiro", linha.MotivoIsencao);

            await operador.RemoverIsencaoAsync(conta.Id, null, CancellationToken.None);
            db.ChangeTracker.Clear();
            await Assert.ThrowsAsync<RegraNegocioException>(() => cobranca.GarantirPodeEmitirEmProducaoAsync(conta.Id, CancellationToken.None));

            // Com assinatura viva no Asaas, nada disso vale (as faturas são de lá).
            db.Assinaturas.Add(new Assinatura { TenantId = assinante.Id, Status = StatusAssinatura.Ativa, AsaasAssinaturaId = "sub_op_" + Guid.NewGuid().ToString("N") });
            await db.SaveChangesAsync();
            var erro = await Assert.ThrowsAsync<RegraNegocioException>(() =>
                operador.IsentarAsync(assinante.Id, new IsentarContaRequest(null, "x"), null, CancellationToken.None));
            Assert.Contains("Asaas", erro.Message);
            await Assert.ThrowsAsync<RegraNegocioException>(() =>
                operador.ProrrogarTesteAsync(assinante.Id, new ProrrogarTesteRequest(5, "x"), null, CancellationToken.None));
        }
        finally
        {
            await LimparAsync(db, cnpj, conta.Id, assinante.Id);
        }
    }
}
