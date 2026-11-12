using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Application.UseCases.IndicesEconomicos;
using NfseSaaS.Application.UseCases.ReajustesContrato;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// Simular reajuste pelo índice na tela do Histórico de valores: acumulado
/// de 12 meses, só a parte fixa, regra do negativo — e registrar o valor
/// sugerido dá exatamente o simulado. Índices em 1990 para não cruzar com
/// os meses reais da tabela.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class SimulacaoReajusteContratoTests
{
    private readonly AplicacaoTesteFactory _factory;

    public SimulacaoReajusteContratoTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private static readonly IndiceReajusteContrato[] IndicesDoTeste = { IndiceReajusteContrato.Incc, IndiceReajusteContrato.Inpc };

    [Fact]
    public async Task Simula_pelo_acumulado_so_na_parte_fixa_e_registra_o_mesmo_valor()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Simular " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        async Task LimparIndices() => await db.IndicesEconomicosMensais
            .Where(i => IndicesDoTeste.Contains(i.Indice) && i.Competencia.Year == 1990)
            .ExecuteDeleteAsync();

        try
        {
            await LimparIndices();
            for (var mes = 1; mes <= 12; mes++)
            {
                db.IndicesEconomicosMensais.Add(new IndiceEconomicoMensal { Indice = IndiceReajusteContrato.Incc, Competencia = new DateOnly(1990, mes, 1), Variacao = 1m });
                db.IndicesEconomicosMensais.Add(new IndiceEconomicoMensal { Indice = IndiceReajusteContrato.Inpc, Competencia = new DateOnly(1990, mes, 1), Variacao = -0.5m });
            }

            var empresa = new Empresa { Cnpj = s[..14], RazaoSocial = "Empresa " + s };
            db.Empresas.Add(empresa);
            await db.SaveChangesAsync();
            var cliente = new Cliente { EmpresaId = empresa.Id, Nome = "Cliente", Apelido = "CLI" + s[..6], CpfCnpj = "52998224725" };
            var manutencao = new Servico { EmpresaId = empresa.Id, Descricao = "Manutenção", CodigoTributacaoNacional = "010501", CodigoNbs = "115013000" };
            var helpDesk = new Servico { EmpresaId = empresa.Id, Descricao = "HelpDesk", CodigoTributacaoNacional = "010501", CodigoNbs = "115013000" };
            db.Clientes.Add(cliente);
            db.Servicos.AddRange(manutencao, helpDesk);
            await db.SaveChangesAsync();

            var audit = sp.GetRequiredService<IAuditLogWriter>();
            var contratoId = await new CadastrarContratoUseCase(db, audit).ExecutarAsync(new CadastrarContratoRequest(
                empresa.Id, cliente.Id, "Suporte mensal",
                new[] { new ContratoServicoRequest(manutencao.Id, 1, 1000m), new ContratoServicoRequest(helpDesk.Id, 1, 500m, ValorVariavel: true) },
                new DateOnly(1990, 1, 10), 12, IndiceReajusteContrato.Incc, null, null, StatusContrato.Ativo, null,
                TipoCobrancaContrato.Mensal, PermitirAlterarValorNaEmissao: false), CancellationToken.None);

            var simular = new SimularReajusteContratoUseCase(db, sp.GetRequiredService<ICalcularIndiceAcumuladoUseCase>());

            // Ao abrir: índice e data do contrato (aniversário vencido 10/01/1991).
            var r = await simular.ExecutarAsync(contratoId, new SimularReajusteRequest(null, null, null), CancellationToken.None);
            Assert.True(r.Disponivel, r.Mensagem);
            Assert.Equal(new DateOnly(1991, 1, 10), r.DataSugerida);
            Assert.Equal(r.DataSugerida, r.DataReajuste);
            Assert.Equal(IndiceReajusteContrato.Incc, r.Indice);
            Assert.Equal(new DateOnly(1990, 1, 1), r.CompetenciaInicio);
            Assert.Equal(new DateOnly(1990, 12, 1), r.CompetenciaFim);
            Assert.Equal(12.6825m, r.PercentualIndice);
            Assert.Equal(500m, r.ParteVariavel);
            Assert.Equal(1626.83m, r.ValorNovo); // 500 + 1000 × 1,126825
            Assert.Equal(new[] { "HelpDesk" }, r.ServicosForaDoReajuste);
            Assert.Equal(12, r.Meses.Count);

            // Negativo: contrato não reduz → mantém; a tela pode escolher reduzir.
            var negativo = await simular.ExecutarAsync(contratoId,
                new SimularReajusteRequest(IndiceReajusteContrato.Inpc, new DateOnly(1991, 1, 10), null), CancellationToken.None);
            Assert.True(negativo.NegativoMantido);
            Assert.Equal(0m, negativo.PercentualAplicado);
            Assert.Equal(1500m, negativo.ValorNovo);
            var reduzindo = await simular.ExecutarAsync(contratoId,
                new SimularReajusteRequest(IndiceReajusteContrato.Inpc, new DateOnly(1991, 1, 10), true), CancellationToken.None);
            Assert.False(reduzindo.NegativoMantido);
            Assert.True(reduzindo.ValorNovo < 1500m);
            Assert.True(reduzindo.ValorNovo > 1000m); // a parte variável não cai

            // Sem tabela ou índice parado: explica em vez de inventar número.
            Assert.False((await simular.ExecutarAsync(contratoId,
                new SimularReajusteRequest(IndiceReajusteContrato.Outro, null, null), CancellationToken.None)).Disponivel);
            var parado = await simular.ExecutarAsync(contratoId,
                new SimularReajusteRequest(IndiceReajusteContrato.Incc, new DateOnly(1992, 6, 10), null), CancellationToken.None);
            Assert.False(parado.Disponivel);
            Assert.NotNull(parado.Mensagem);

            // Registrar o valor sugerido: só a linha fixa sobe, o % é sobre a parte fixa.
            await new RegistrarReajusteUseCase(db, audit, sp.GetRequiredService<ICurrentUser>()).ExecutarAsync(contratoId,
                new RegistrarReajusteRequest(r.DataReajuste, r.ValorNovo!.Value, IndiceReajusteContrato.Incc, "teste"), CancellationToken.None);
            db.ChangeTracker.Clear();
            var linhas = await db.ContratoServicos.AsNoTracking().Where(l => l.ContratoId == contratoId).ToListAsync();
            Assert.Equal(1126.83m, linhas.Single(l => l.ServicoId == manutencao.Id).ValorUnitario);
            Assert.Equal(500m, linhas.Single(l => l.ServicoId == helpDesk.Id).ValorUnitario);
            var reajuste = await db.MovimentacoesValorContrato.AsNoTracking()
                .SingleAsync(m => m.ContratoId == contratoId && m.Tipo == TipoMovimentacaoValor.Reajuste);
            Assert.Equal(12.683m, reajuste.PercentualAplicado);
            Assert.Equal(1626.83m, (await db.Contratos.AsNoTracking().SingleAsync(c => c.Id == contratoId)).ValorAtual);

            // Contrato cadastrado com R$ 1 e corrigido para R$ 2.000: grava
            // (antes: "numeric field overflow"), sem percentual sem sentido.
            var marcadorId = await new CadastrarContratoUseCase(db, audit).ExecutarAsync(new CadastrarContratoRequest(
                empresa.Id, cliente.Id, "Cadastrado com R$ 1",
                new[] { new ContratoServicoRequest(manutencao.Id, 1, 1m) },
                new DateOnly(2026, 1, 1), 12, null, null, null, StatusContrato.Ativo, null,
                TipoCobrancaContrato.Mensal, PermitirAlterarValorNaEmissao: false), CancellationToken.None);
            await new RegistrarReajusteUseCase(db, audit, sp.GetRequiredService<ICurrentUser>()).ExecutarAsync(marcadorId,
                new RegistrarReajusteRequest(new DateOnly(2026, 11, 1), 2000m, null, null), CancellationToken.None);
            db.ChangeTracker.Clear();
            var correcao = await db.MovimentacoesValorContrato.AsNoTracking()
                .SingleAsync(m => m.ContratoId == marcadorId && m.Tipo == TipoMovimentacaoValor.Reajuste);
            Assert.Equal(2000m, correcao.ValorNovo);
            Assert.Null(correcao.PercentualAplicado);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await LimparIndices();
            await db.MovimentacoesValorContrato.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.ContratoServicos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Contratos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Clientes.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Servicos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }
}
