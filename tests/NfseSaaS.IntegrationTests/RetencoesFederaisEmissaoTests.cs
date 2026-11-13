using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.Fiscal.Retencoes;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using NfseSaaS.Nacional.Models;
using NfseSaaS.Nacional.Options;
using NfseSaaS.Nacional.Responses;
using NfseSaaS.Nacional.Services;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// C6 com o banco real e a SEFIN falsa: retenções do cliente na automação
/// (sem o que o regime dispensa), escolha na nota, descontos, tpRetPisCofins
/// calculado, frase de dispensa e líquido negativo barrado antes da SEFIN.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class RetencoesFederaisEmissaoTests
{
    private readonly AplicacaoTesteFactory _factory;

    public RetencoesFederaisEmissaoTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class SefinFalsa : INfseNacionalService
    {
        public List<DpsRequest> Enviadas { get; } = new();

        public Task<NfseNacionalResponse> EmitirAsync(DpsRequest request, Guid empresaId, CancellationToken cancellationToken)
        {
            Enviadas.Add(request);
            return Task.FromResult(new NfseNacionalResponse(true, 2, "teste", DateTimeOffset.UtcNow, "DPS" + request.NumeroDps,
                ChaveUnica(), null, Array.Empty<NfseErro>()));
        }

        // Cada teste cria uma conta nova (DPS começa em 1): a chave precisa ser única no banco.
        private static string ChaveUnica() =>
            "2919207220" + string.Concat(Enumerable.Range(0, 40).Select(_ => Random.Shared.Next(10)));

        public Task<EventoNacionalResponse> CancelarAsync(EventoCancelamentoRequest request, Guid empresaId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CobrancaLivre : IVerificadorCobranca
    {
        public Task GarantirPodeEmitirEmProducaoAsync(Guid tenantId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static EmitirNfseUseCase Emissao(IServiceProvider sp, SefinFalsa sefin) => new(
        sp.GetRequiredService<AppDbContext>(),
        sefin,
        sp.GetRequiredService<IAuditLogWriter>(),
        sp.GetRequiredService<INfseEventoWriter>(),
        sp.GetRequiredService<IEnviarNfsePorEmailUseCase>(),
        NullLogger<EmitirNfseUseCase>.Instance,
        new CobrancaLivre(),
        sp.GetRequiredService<IOptions<NfseNacionalOptions>>());

    private sealed record Cenario(Empresa Empresa, Cliente Cliente, Servico Servico);

    private static async Task<Cenario> CriarAsync(IServiceProvider sp, string opSimpNac, bool fraseDispensa, PercentuaisRetencao retencoesCliente)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Retencoes " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        var empresa = new Empresa
        {
            Cnpj = "04747304000178", RazaoSocial = "Empresa " + s, InscricaoMunicipal = "12345",
            CodigoMunicipio = "2919207", Uf = "BA", Cep = "42700000", Logradouro = "Rua A", Numero = "1", Bairro = "Centro",
            Telefone = "71999999999", Email = "empresa@teste.com",
            OpSimpNac = opSimpNac, RegApTribSN = "1", RegEspTrib = "0", TribIssqn = "1", CstPisCofins = "00",
            PercentualTotalTributosSimplesNacional = opSimpNac == "1" ? string.Empty : "6.00",
            AliquotaIss = 5.00m,
            AvisoDispensaRetencaoNaDescricao = fraseDispensa
        };
        db.Empresas.Add(empresa);
        await db.SaveChangesAsync();

        var cliente = new Cliente
        {
            EmpresaId = empresa.Id, CpfCnpj = "11222333000181", Nome = "Tomador Grande SA", Apelido = "Tom " + s[..6],
            Cep = "42700000", Logradouro = "Rua B", Numero = "2", Bairro = "Centro", CodigoMunicipio = "2919207", Uf = "BA"
        };
        RetencoesCliente.Aplicar(cliente, retencoesCliente);
        var servico = new Servico { EmpresaId = empresa.Id, Descricao = "Consultoria", CodigoTributacaoNacional = "010501", CodigoNbs = "115013000" };
        db.Clientes.Add(cliente);
        db.Servicos.Add(servico);
        await db.SaveChangesAsync();
        return new Cenario(empresa, cliente, servico);
    }

    private static readonly PercentuaisRetencao Todas = new(1.5m, 0.65m, 3m, 1m, 11m);

    /// <summary>Apaga a conta do teste (mesma limpeza dos outros testes de emissão).</summary>
    private static async Task LimparAsync(IServiceProvider sp, Guid tenantId)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        db.ChangeTracker.Clear();
        await db.NfseEventos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.EmailLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.NotasFiscais.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Clientes.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Servicos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM contadores_dps WHERE \"EmpresaId\" IN (SELECT \"Id\" FROM empresas WHERE \"TenantId\" = {tenantId})");
        await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Tenants.Where(x => x.Id == tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Nao_optante_na_automacao_usa_as_retencoes_do_cliente()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var c = await CriarAsync(sp, "1", fraseDispensa: false, Todas);
        try
        {
            var sefin = new SefinFalsa();

            var resultado = await Emissao(sp, sefin).ExecutarAsync(new EmitirNfseRequest(
                c.Empresa.Id, c.Cliente.Id, c.Servico.Id, 10_000m, "Consultoria de outubro", new DateOnly(2026, 11, 1),
                DescontoIncondicionado: 1_000m, DescontoCondicionado: 100m), CancellationToken.None);

            Assert.True(resultado.Sucesso);
            var dps = Assert.Single(sefin.Enviadas);
            // Base 9.000: IRRF 135, PIS 58,50 + COFINS 270 + CSLL 90 = 418,50, INSS 990.
            Assert.Equal("3", dps.Tributacao.TpRetPisCofins);
            Assert.Equal(new RetencoesFederaisDps(990.00m, 135.00m, 418.50m), dps.Tributacao.RetencoesFederais);
            Assert.Equal(new DescontosDps(1_000m, 100m), dps.Descontos);

            var nfse = await sp.GetRequiredService<AppDbContext>().NotasFiscais.AsNoTracking().SingleAsync(n => n.Id == resultado.NfseId);
            Assert.Equal(135.00m, nfse.ValorRetidoIrrf);
            Assert.Equal(58.50m, nfse.ValorRetidoPis);
            Assert.Equal(270.00m, nfse.ValorRetidoCofins);
            Assert.Equal(90.00m, nfse.ValorRetidoCsll);
            Assert.Equal(990.00m, nfse.ValorRetidoInss);
            Assert.Equal(1_000m, nfse.DescontoIncondicionado);
            Assert.Equal("3", nfse.TpRetPisCofins);
        }
        finally
        {
            await LimparAsync(sp, c.Empresa.TenantId);
        }
    }

    [Fact]
    public async Task Simples_na_automacao_nao_retem_o_dispensado_e_ganha_a_frase()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var c = await CriarAsync(sp, "3", fraseDispensa: true, Todas with { Inss = null });
        try
        {
            var sefin = new SefinFalsa();

            var resultado = await Emissao(sp, sefin).ExecutarAsync(new EmitirNfseRequest(
                c.Empresa.Id, c.Cliente.Id, c.Servico.Id, 10_000m, "Consultoria de outubro", new DateOnly(2026, 11, 1)), CancellationToken.None);

            var dps = Assert.Single(sefin.Enviadas);
            Assert.Equal("0", dps.Tributacao.TpRetPisCofins);
            Assert.Equal(new RetencoesFederaisDps(null, null, null), dps.Tributacao.RetencoesFederais);
            Assert.EndsWith(RegrasRetencoesFederais.FraseDispensaSimples, dps.DescricaoServico);

            var nfse = await sp.GetRequiredService<AppDbContext>().NotasFiscais.AsNoTracking().SingleAsync(n => n.Id == resultado.NfseId);
            Assert.Null(nfse.ValorRetidoIrrf);
            Assert.EndsWith(RegrasRetencoesFederais.FraseDispensaSimples, nfse.DescricaoServico);
        }
        finally
        {
            await LimparAsync(sp, c.Empresa.TenantId);
        }
    }

    [Fact]
    public async Task Na_tela_o_usuario_decide_e_pode_reter_mesmo_no_simples()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var c = await CriarAsync(sp, "3", fraseDispensa: true, PercentuaisRetencao.Nenhum);
        try
        {
            var sefin = new SefinFalsa();

            await Emissao(sp, sefin).ExecutarAsync(new EmitirNfseRequest(
                c.Empresa.Id, c.Cliente.Id, c.Servico.Id, 1_000m, "Consultoria", new DateOnly(2026, 11, 1),
                RetencoesFederais: new PercentuaisRetencao(Csll: 1m), ReterAbaixoDoMinimo: true), CancellationToken.None);

            var dps = Assert.Single(sefin.Enviadas);
            Assert.Equal("8", dps.Tributacao.TpRetPisCofins);
            Assert.Equal(10.00m, dps.Tributacao.RetencoesFederais!.VRetCSLL);
            Assert.DoesNotContain(RegrasRetencoesFederais.FraseDispensaSimples, dps.DescricaoServico);
        }
        finally
        {
            await LimparAsync(sp, c.Empresa.TenantId);
        }
    }

    [Fact]
    public async Task Liquido_negativo_para_antes_da_sefin_e_nao_gasta_numero()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var c = await CriarAsync(sp, "1", fraseDispensa: false, PercentuaisRetencao.Nenhum);
        try
        {
            var sefin = new SefinFalsa();

            var ex = await Assert.ThrowsAsync<RegraNegocioException>(() => Emissao(sp, sefin).ExecutarAsync(new EmitirNfseRequest(
                c.Empresa.Id, c.Cliente.Id, c.Servico.Id, 100m, "Consultoria", new DateOnly(2026, 11, 1),
                RetencoesFederais: new PercentuaisRetencao(Irrf: 60m, Inss: 50m)), CancellationToken.None));

            Assert.Contains("líquido", ex.Message);
            Assert.Empty(sefin.Enviadas);
            Assert.False(await sp.GetRequiredService<AppDbContext>().NotasFiscais.AnyAsync(n => n.EmpresaId == c.Empresa.Id));
        }
        finally
        {
            await LimparAsync(sp, c.Empresa.TenantId);
        }
    }

    [Fact]
    public async Task Simulacao_usa_o_padrao_do_cliente_e_avisa_o_regime()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var c = await CriarAsync(sp, "3", fraseDispensa: true, Todas);
        try
        {
            var simular = sp.GetRequiredService<ISimularRetencoesUseCase>();

            var padrao = await simular.ExecutarAsync(new SimularRetencoesRequest(c.Empresa.Id, c.Cliente.Id, 10_000m), CancellationToken.None);
            Assert.Equal(new PercentuaisRetencao(Inss: 11m), padrao.Percentuais);
            Assert.Equal(1_100m, padrao.Resultado!.Inss);
            Assert.Null(padrao.FraseDispensa);
            Assert.True(padrao.RetencaoFederalDisponivel);

            var naMao = await simular.ExecutarAsync(new SimularRetencoesRequest(c.Empresa.Id, c.Cliente.Id, 10_000m,
                RetencoesFederais: new PercentuaisRetencao(Irrf: 1.5m), IssRetido: true), CancellationToken.None);
            Assert.Contains(naMao.Resultado!.Alertas, a => a.Contains("765/2007"));
            Assert.Equal(500m, naMao.Resultado.IssRetido);
            Assert.Equal(9_350m, naMao.Resultado.ValorLiquido);

            var semRetencao = await simular.ExecutarAsync(new SimularRetencoesRequest(c.Empresa.Id, c.Cliente.Id, 10_000m,
                RetencoesFederais: PercentuaisRetencao.Nenhum), CancellationToken.None);
            Assert.Equal(RegrasRetencoesFederais.FraseDispensaSimples, semRetencao.FraseDispensa);

            var erro = await simular.ExecutarAsync(new SimularRetencoesRequest(c.Empresa.Id, c.Cliente.Id, 100m,
                DescontoIncondicionado: 100m), CancellationToken.None);
            Assert.NotNull(erro.Erro);
            Assert.Null(erro.Resultado);
        }
        finally
        {
            await LimparAsync(sp, c.Empresa.TenantId);
        }
    }
}
