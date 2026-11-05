using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Cobranca;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// Catálogo de planos no banco: planos iniciais, oferta por perfil, vagas de
/// fundador separadas por grupo, preço com vigência/histórico e o preço
/// contratado preservado.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class CatalogoPlanosTests
{
    private readonly AplicacaoTesteFactory _factory;

    public CatalogoPlanosTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class GatewayFalso : IGatewayCobranca
    {
        public List<decimal> Valores { get; } = new();
        public bool Configurado => true;
        public Task<string> CriarClienteAsync(string nome, string cpfCnpj, string? email, string referenciaExterna, CancellationToken cancellationToken) =>
            Task.FromResult("cus_" + Guid.NewGuid().ToString("N"));
        public Task<string> CriarAssinaturaMensalAsync(string clienteId, decimal valor, DateOnly primeiroVencimento, string descricao, string referenciaExterna, CancellationToken cancellationToken)
        {
            Valores.Add(valor);
            return Task.FromResult("sub_" + Guid.NewGuid().ToString("N"));
        }
        public Task<string?> ObterUrlFaturaPendenteAsync(string assinaturaId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task CancelarAssinaturaAsync(string assinaturaId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<FaturaGateway?> ObterProximaFaturaEmAbertoAsync(string assinaturaId, string? ignorarPagamentoId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RemoverFaturaAsync(string pagamentoId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DateOnly?> ObterProximoVencimentoAsync(string assinaturaId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AlterarProximoVencimentoAsync(string assinaturaId, DateOnly novoVencimento, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static CobrancaService Servico(IServiceProvider sp, IGatewayCobranca gateway) => new(
        sp.GetRequiredService<AppDbContext>(),
        sp.GetRequiredService<ICurrentTenant>(),
        sp.GetRequiredService<ICurrentUser>(),
        gateway,
        sp.GetRequiredService<IAuditLogWriter>(),
        sp.GetRequiredService<ICatalogoPlanos>(),
        Options.Create(new CobrancaOptions { Ativa = true }),
        NullLogger<CobrancaService>.Instance);

    [Fact]
    public async Task Planos_iniciais_existem_com_preco_e_vagas_separadas()
    {
        using var escopo = _factory.Services.CreateScope();
        var admin = escopo.ServiceProvider.GetRequiredService<IAdministrarPlanosUseCase>();

        var planos = await admin.ListarAsync(CancellationToken.None);
        var mico = Assert.Single(planos, p => p.Codigo == Planos.CodigoMico);
        Assert.True(mico.OfertarParaEmpresa);
        Assert.False(mico.OfertarParaContador);
        Assert.NotNull(mico.PrecoMensal);

        var escritorio = Assert.Single(planos, p => p.Codigo == Planos.CodigoEscritorio10);
        Assert.Equal(PublicoPlano.Escritorios, escritorio.Publico);
        Assert.Null(escritorio.NotasPorMes);
        Assert.Equal(10, escritorio.MaxEmpresas);
        Assert.Contains(planos, p => p.Codigo == Planos.CodigoEscritorio25);
        Assert.Contains(planos, p => p.Codigo == Planos.CodigoEscritorio50);

        var vagas = await admin.ListarVagasAsync(CancellationToken.None);
        Assert.Contains(vagas, v => v.Publico == PublicoPlano.Empresas);
        Assert.Contains(vagas, v => v.Publico == PublicoPlano.Escritorios);
    }

    [Fact]
    public async Task Contador_ve_os_planos_de_escritorio_assina_com_preco_de_fundador_do_grupo_e_nao_assina_o_Mico()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var admin = sp.GetRequiredService<IAdministrarPlanosUseCase>();
        var catalogo = sp.GetRequiredService<ICatalogoPlanos>();

        var s = Guid.NewGuid().ToString("N");
        var contador = new Tenant { Documento = s[..11], RazaoSocial = "Contador " + s, Perfil = PerfilConta.Contador };
        db.Tenants.Add(contador);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(contador.Id);
        var vagasAntes = (await admin.ListarVagasAsync(CancellationToken.None)).Single(v => v.Publico == PublicoPlano.Escritorios);
        var gateway = new GatewayFalso();

        try
        {
            IObterAssinaturaUseCase obter = Servico(sp, gateway);
            var tela = await obter.ExecutarAsync(CancellationToken.None);
            var codigos = tela.Planos.Select(p => p.Codigo).ToList();
            Assert.DoesNotContain(Planos.CodigoMico, codigos);
            Assert.Contains(Planos.CodigoEmpresa, codigos);
            Assert.Contains(Planos.CodigoEscritorio10, codigos);

            IAssinarUseCase assinar = Servico(sp, gateway);
            await Assert.ThrowsAsync<RegraNegocioException>(() => assinar.ExecutarAsync(new AssinarRequest(Planos.CodigoMico), CancellationToken.None));

            // Garante 1 vaga sobrando no grupo dos escritórios para o teste.
            await admin.AlterarVagasAsync(new AlterarVagasFundadorRequest(PublicoPlano.Escritorios, vagasAntes.Usadas + 1, "teste"), null, CancellationToken.None);
            var escritorio = (await catalogo.ObterAsync(Planos.CodigoEscritorio10, CancellationToken.None))!;
            var restantesEmpresas = await catalogo.VagasFundadorRestantesAsync(PublicoPlano.Empresas, CancellationToken.None);

            await assinar.ExecutarAsync(new AssinarRequest(Planos.CodigoEscritorio10), CancellationToken.None);

            var assinatura = await db.Assinaturas.AsNoTracking().SingleAsync(a => a.TenantId == contador.Id);
            Assert.Equal(Planos.CodigoEscritorio10, assinatura.Plano);
            Assert.True(assinatura.Fundador);
            Assert.Equal(escritorio.PrecoFundador, assinatura.PrecoMensal);
            Assert.Equal([escritorio.PrecoFundador], gateway.Valores);

            // A vaga saiu do grupo dos escritórios, não do das empresas.
            Assert.Equal(0, await catalogo.VagasFundadorRestantesAsync(PublicoPlano.Escritorios, CancellationToken.None));
            Assert.Equal(restantesEmpresas, await catalogo.VagasFundadorRestantesAsync(PublicoPlano.Empresas, CancellationToken.None));

            // Sem vaga, a tela oferece o preço normal e o nome do plano contratado aparece.
            tela = await obter.ExecutarAsync(CancellationToken.None);
            var oferta = tela.Planos.Single(p => p.Codigo == Planos.CodigoEscritorio25);
            Assert.False(oferta.PrecoDeFundador);
            Assert.Equal(oferta.PrecoNormal, oferta.Preco);
            Assert.Equal(escritorio.Nome, tela.PlanoNome);
            Assert.Equal(0, tela.VagasFundadorRestantes);
        }
        finally
        {
            await admin.AlterarVagasAsync(new AlterarVagasFundadorRequest(PublicoPlano.Escritorios, vagasAntes.Quantidade, "teste"), null, CancellationToken.None);
            await db.Assinaturas.Where(a => a.TenantId == contador.Id).ExecuteDeleteAsync();
            await db.AuditLogs.IgnoreQueryFilters().Where(a => a.TenantId == contador.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(t => t.Id == contador.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Preco_novo_tem_historico_agendamento_e_correcao_na_mesma_data()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var admin = sp.GetRequiredService<IAdministrarPlanosUseCase>();
        var catalogo = sp.GetRequiredService<ICatalogoPlanos>();
        var hoje = DataBrasilia.Hoje();

        // Na tela, o Histórico fica na conta do operador logado.
        var operador = new Tenant { Documento = Guid.NewGuid().ToString("N")[..14], RazaoSocial = "Operador de teste" };
        db.Tenants.Add(operador);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(operador.Id);

        // Plano fora de venda: não aparece para as outras contas de teste.
        var criado = await admin.CriarAsync(
            new SalvarPlanoRequest("Teste " + Guid.NewGuid().ToString("N")[..6], "plano de teste", PublicoPlano.Escritorios, false, true, 5, null, false, 999, 100m, 80m),
            null, CancellationToken.None);

        try
        {
            Assert.Equal(100m, criado.PrecoMensal);
            Assert.Equal(hoje, criado.VigenteDesde);

            await admin.AdicionarPrecoAsync(criado.Id, new NovoPrecoPlanoRequest(120m, 90m, hoje.AddDays(30), "reajuste"), null, CancellationToken.None);
            await admin.AdicionarPrecoAsync(criado.Id, new NovoPrecoPlanoRequest(110m, 85m, hoje, "correção"), null, CancellationToken.None);
            await Assert.ThrowsAsync<RegraNegocioException>(() =>
                admin.AdicionarPrecoAsync(criado.Id, new NovoPrecoPlanoRequest(90m, 70m, hoje.AddDays(-1), null), null, CancellationToken.None));

            var linha = (await admin.ListarAsync(CancellationToken.None)).Single(p => p.Id == criado.Id);
            Assert.Equal(110m, linha.PrecoMensal);
            Assert.Equal(85m, linha.PrecoFundador);
            Assert.Equal(120m, linha.ProximoPrecoMensal);
            Assert.Equal(hoje.AddDays(30), linha.ProximoVigenteDesde);

            var historico = await admin.HistoricoPrecosAsync(criado.Id, CancellationToken.None);
            Assert.Equal(2, historico.Count); // a correção não cria linha nova
            Assert.Equal(["Agendado", "Vigente"], historico.Select(h => h.Situacao));

            var vigente = await catalogo.ObterAsync(criado.Codigo, CancellationToken.None);
            Assert.Equal(110m, vigente!.PrecoMensal);
            Assert.False(vigente.AVenda);

            // Alterar o plano não mexe no preço; grupo só muda sem assinaturas.
            var alterado = await admin.AlterarAsync(criado.Id,
                new SalvarPlanoRequest("Novo nome", "x", PublicoPlano.Empresas, true, false, 6, 50, false, 999, null, null), null, CancellationToken.None);
            Assert.Equal("Novo nome", alterado.Nome);
            Assert.Equal(PublicoPlano.Empresas, alterado.Publico);
            Assert.Equal(110m, alterado.PrecoMensal);
        }
        finally
        {
            await db.PrecosPlanos.Where(p => p.PlanoId == criado.Id).ExecuteDeleteAsync();
            await db.Planos.Where(p => p.Id == criado.Id).ExecuteDeleteAsync();
            await db.AuditLogs.IgnoreQueryFilters().Where(a => a.TenantId == operador.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(t => t.Id == operador.Id).ExecuteDeleteAsync();
        }
    }
}
