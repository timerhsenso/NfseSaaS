using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Cobranca;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>Troca de plano com o banco real e um gateway falso (nunca o Asaas).</summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class TrocaPlanoTests
{
    private readonly AplicacaoTesteFactory _factory;

    public TrocaPlanoTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class GatewayFalso : IGatewayCobranca
    {
        public List<(string AssinaturaId, decimal Valor)> Alteracoes { get; } = new();
        public bool Configurado => true;
        public Task AlterarValorAssinaturaAsync(string assinaturaId, decimal valor, string descricao, CancellationToken cancellationToken)
        {
            Alteracoes.Add((assinaturaId, valor));
            return Task.CompletedTask;
        }
        public Task<string> CriarClienteAsync(string nome, string cpfCnpj, string? email, string referenciaExterna, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> CriarAssinaturaMensalAsync(string clienteId, decimal valor, DateOnly primeiroVencimento, string descricao, string referenciaExterna, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ObterUrlFaturaPendenteAsync(string assinaturaId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CancelarAssinaturaAsync(string assinaturaId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FaturaGateway?> ObterProximaFaturaEmAbertoAsync(string assinaturaId, string? ignorarPagamentoId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RemoverFaturaAsync(string pagamentoId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DateOnly?> ObterProximoVencimentoAsync(string assinaturaId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AssinaturaGatewayConciliacao?> ObterParaConciliacaoAsync(string assinaturaId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AlterarProximoVencimentoAsync(string assinaturaId, DateOnly novoVencimento, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static CobrancaService Servico(IServiceProvider sp, IGatewayCobranca gateway, CobrancaOptions opcoes) => new(
        sp.GetRequiredService<AppDbContext>(),
        sp.GetRequiredService<ICurrentTenant>(),
        sp.GetRequiredService<ICurrentUser>(),
        gateway,
        sp.GetRequiredService<IAuditLogWriter>(),
        sp.GetRequiredService<ICatalogoPlanos>(),
        Options.Create(opcoes),
        NullLogger<CobrancaService>.Instance);

    [Fact]
    public async Task Fundador_sobe_de_plano_mantendo_preco_de_fundador_e_nao_desce_com_empresas_demais()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var catalogo = sp.GetRequiredService<ICatalogoPlanos>();
        var mico = (await catalogo.ObterAsync(Planos.CodigoMico, CancellationToken.None))!;
        var empresa = (await catalogo.ObterAsync(Planos.CodigoEmpresa, CancellationToken.None))!;

        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Troca plano " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        var assinatura = new Assinatura
        {
            TenantId = conta.Id,
            Status = StatusAssinatura.Ativa,
            Plano = Planos.CodigoMico,
            PrecoMensal = mico.PrecoFundador,
            Fundador = true,
            AsaasAssinaturaId = "sub_tp_" + s,
            UrlFaturaPendente = "https://asaas.teste/i/" + s,
            ValorFaturaPendente = mico.PrecoFundador
        };
        db.Assinaturas.Add(assinatura);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);
        var gateway = new GatewayFalso();

        try
        {
            // Beta (cobrança desligada): não troca.
            ITrocarPlanoUseCase desligada = Servico(sp, gateway, new CobrancaOptions { Ativa = false });
            await Assert.ThrowsAsync<RegraNegocioException>(() => desligada.ExecutarAsync(new TrocarPlanoRequest(Planos.CodigoEmpresa), CancellationToken.None));

            var ligada = Servico(sp, gateway, new CobrancaOptions { Ativa = true });
            var tela = await ((IObterAssinaturaUseCase)ligada).ExecutarAsync(CancellationToken.None);
            Assert.True(tela.PodeTrocarPlano);
            Assert.False(tela.PodeAssinar);
            Assert.Equal(empresa.PrecoFundador, tela.Planos.Single(p => p.Codigo == Planos.CodigoEmpresa).Preco);

            var r = await ((ITrocarPlanoUseCase)ligada).ExecutarAsync(new TrocarPlanoRequest(Planos.CodigoEmpresa), CancellationToken.None);
            Assert.Equal(empresa.PrecoFundador, r.PrecoMensal);
            Assert.True(r.Fundador);
            Assert.Equal(("sub_tp_" + s, empresa.PrecoFundador), Assert.Single(gateway.Alteracoes));

            db.ChangeTracker.Clear();
            var salva = await db.Assinaturas.AsNoTracking().SingleAsync(a => a.TenantId == conta.Id);
            Assert.Equal(Planos.CodigoEmpresa, salva.Plano);
            Assert.Equal(empresa.PrecoFundador, salva.PrecoMensal);
            Assert.Equal(empresa.PrecoFundador, salva.ValorFaturaPendente);
            Assert.True(await db.AuditLogs.AnyAsync(l => l.Operacao == "TrocarPlano" && l.EntidadeId == salva.Id));

            // Mesmo plano: recusa.
            await Assert.ThrowsAsync<RegraNegocioException>(() => ((ITrocarPlanoUseCase)ligada).ExecutarAsync(new TrocarPlanoRequest(Planos.CodigoEmpresa), CancellationToken.None));

            // Duas empresas ativas: não desce para o Mico (1 empresa).
            db.TenantIdOverrideDeSistema = conta.Id;
            db.Empresas.Add(new Empresa { Cnpj = s[..14], RazaoSocial = "E1 " + s });
            db.Empresas.Add(new Empresa { Cnpj = s[14..28], RazaoSocial = "E2 " + s });
            await db.SaveChangesAsync();
            db.TenantIdOverrideDeSistema = null;
            var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => ((ITrocarPlanoUseCase)ligada).ExecutarAsync(new TrocarPlanoRequest(Planos.CodigoMico), CancellationToken.None));
            Assert.Contains("2 empresa(s) ativa(s)", erro.Message);
            Assert.Single(gateway.Alteracoes);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Assinaturas.Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Sem_assinatura_no_gateway_nao_troca()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Sem assinatura " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        try
        {
            ITrocarPlanoUseCase servico = Servico(sp, new GatewayFalso(), new CobrancaOptions { Ativa = true });
            var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => servico.ExecutarAsync(new TrocarPlanoRequest(Planos.CodigoEmpresa), CancellationToken.None));
            Assert.Contains("não tem assinatura ativa", erro.Message);
        }
        finally
        {
            await db.Assinaturas.Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }
}
