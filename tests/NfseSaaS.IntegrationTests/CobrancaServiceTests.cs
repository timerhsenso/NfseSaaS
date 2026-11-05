using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Cobranca;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>Verificação da emissão em produção com o banco real e a cobrança ligada.</summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class CobrancaServiceTests
{
    private readonly AplicacaoTesteFactory _factory;

    public CobrancaServiceTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private static CobrancaService Servico(IServiceProvider sp, CobrancaOptions opcoes) => new(
        sp.GetRequiredService<AppDbContext>(),
        sp.GetRequiredService<ICurrentTenant>(),
        sp.GetRequiredService<ICurrentUser>(),
        sp.GetRequiredService<IGatewayCobranca>(),
        sp.GetRequiredService<IAuditLogWriter>(),
        sp.GetRequiredService<ICatalogoPlanos>(),
        Options.Create(opcoes),
        NullLogger<CobrancaService>.Instance);

    [Fact]
    public async Task Primeira_nota_inicia_o_teste_e_teste_vencido_bloqueia_exceto_conta_isenta_ou_cobranca_desligada()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();

        var tenant = new Tenant { Documento = Guid.NewGuid().ToString("N")[..14], RazaoSocial = "Teste cobrança" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        try
        {
            var ligada = Servico(sp, new CobrancaOptions { Ativa = true });

            await ligada.GarantirPodeEmitirEmProducaoAsync(tenant.Id, CancellationToken.None);
            var assinatura = await db.Assinaturas.SingleAsync(a => a.TenantId == tenant.Id);
            Assert.NotNull(assinatura.TesteIniciadoEm);

            assinatura.TesteIniciadoEm = DateTimeOffset.UtcNow.AddDays(-31);
            await db.SaveChangesAsync();

            var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => ligada.GarantirPodeEmitirEmProducaoAsync(tenant.Id, CancellationToken.None));
            Assert.Contains("teste grátis terminou", erro.Message);

            await Servico(sp, new CobrancaOptions { Ativa = true, ContasIsentas = { tenant.Id } })
                .GarantirPodeEmitirEmProducaoAsync(tenant.Id, CancellationToken.None);
            await Servico(sp, new CobrancaOptions { Ativa = false })
                .GarantirPodeEmitirEmProducaoAsync(tenant.Id, CancellationToken.None);

            // Conta piloto: cobrança ligada só para ela, mesmo com Ativa=false.
            await Assert.ThrowsAsync<RegraNegocioException>(() => Servico(sp, new CobrancaOptions { Ativa = false, ContasPiloto = { tenant.Id } })
                .GarantirPodeEmitirEmProducaoAsync(tenant.Id, CancellationToken.None));
            await Servico(sp, new CobrancaOptions { Ativa = false, ContasPiloto = { Guid.NewGuid() } })
                .GarantirPodeEmitirEmProducaoAsync(tenant.Id, CancellationToken.None);
        }
        finally
        {
            await db.Assinaturas.Where(a => a.TenantId == tenant.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(t => t.Id == tenant.Id).ExecuteDeleteAsync();
        }
    }
}
