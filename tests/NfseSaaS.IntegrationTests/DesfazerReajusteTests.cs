using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Application.UseCases.ReajustesContrato;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// Reajuste registrado antes de sair o índice do mês: o aviso de dois
/// reajustes no mesmo ciclo, "substituir o anterior" (desfaz e aplica o
/// novo na mesma gravação), "Desfazer" pelo histórico e o bloqueio quando
/// já saiu nota de produção com o valor novo.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class DesfazerReajusteTests
{
    private readonly AplicacaoTesteFactory _factory;

    public DesfazerReajusteTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class UsuarioFalso(Guid id) : ICurrentUser
    {
        public Guid? UserId { get; } = id;
        public string? IpAddress => "127.0.0.1";
    }

    private sealed record Cenario(AppDbContext Db, IAuditLogWriter Audit, ICurrentUser Usuario, Guid ContratoId, Guid EmpresaId,
        Guid ClienteId, Guid ManutencaoId, Guid HelpDeskId)
    {
        public RegistrarReajusteUseCase Registrar => new(Db, Audit, Usuario);
        public DesfazerReajusteUseCase Desfazer => new(Db, Audit, Usuario);
        public ListarHistoricoValoresContratoUseCase Historico => new(Db);

        public async Task<Contrato> ContratoAsync()
        {
            Db.ChangeTracker.Clear();
            return await Db.Contratos.AsNoTracking().SingleAsync(c => c.Id == ContratoId);
        }

        public async Task<decimal> LinhaAsync(Guid servicoId)
        {
            Db.ChangeTracker.Clear();
            return (await Db.ContratoServicos.AsNoTracking().SingleAsync(l => l.ContratoId == ContratoId && l.ServicoId == servicoId)).ValorUnitario;
        }
    }

    /// <summary>
    /// Contrato do caso real: manutenção R$ 328,98 desde 11/11/2025, IPCA,
    /// mais um HelpDesk variável de R$ 50 (fica fora do reajuste).
    /// </summary>
    private async Task ExecutarAsync(Func<Cenario, Task> teste)
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Desfazer " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        try
        {
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
                empresa.Id, cliente.Id, "Manutenção RHU",
                new[] { new ContratoServicoRequest(manutencao.Id, 1, 328.98m), new ContratoServicoRequest(helpDesk.Id, 1, 50m, ValorVariavel: true) },
                new DateOnly(2025, 11, 11), 12, IndiceReajusteContrato.Ipca, null, null, StatusContrato.Ativo, null,
                TipoCobrancaContrato.Mensal, PermitirAlterarValorNaEmissao: false), CancellationToken.None);

            await teste(new Cenario(db, audit, new UsuarioFalso(Guid.NewGuid()), contratoId, empresa.Id, cliente.Id, manutencao.Id, helpDesk.Id));
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.NotasFiscais.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
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

    // 4,5761% sobre a parte fixa (328,98) → 344,03 + 50 variável.
    private static RegistrarReajusteRequest ReajusteDeOntem() =>
        new(new DateOnly(2026, 11, 11), 394.03m, IndiceReajusteContrato.Ipca, "IPCA acumulado 4,5761% (out/2025 a set/2026)");

    [Fact]
    public async Task Segundo_reajuste_no_ciclo_pede_escolha_e_substituir_desfaz_o_anterior()
    {
        await ExecutarAsync(async c =>
        {
            var ontemId = await c.Registrar.ExecutarAsync(c.ContratoId, ReajusteDeOntem(), CancellationToken.None);
            Assert.Equal(344.03m, await c.LinhaAsync(c.ManutencaoId));

            // Sem escolha: o servidor não deixa somar sem querer.
            var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => c.Registrar.ExecutarAsync(c.ContratoId,
                new RegistrarReajusteRequest(new DateOnly(2026, 11, 12), 410.95m, IndiceReajusteContrato.Ipca, null), CancellationToken.None));
            Assert.Contains("11/11/2026", erro.Message);

            // Substituir: 4,6284% sobre 328,98 = 344,21 (+ 50 variável).
            c.Db.ChangeTracker.Clear();
            await c.Registrar.ExecutarAsync(c.ContratoId,
                new RegistrarReajusteRequest(new DateOnly(2026, 11, 11), 394.21m, IndiceReajusteContrato.Ipca, "IPCA acumulado 4,6284% (nov/2025 a out/2026)",
                    SubstituirReajusteId: ontemId, MotivoSubstituicao: "Índice de outubro saiu hoje"),
                CancellationToken.None);

            var contrato = await c.ContratoAsync();
            Assert.Equal(394.21m, contrato.ValorAtual);
            Assert.Equal(new DateOnly(2026, 11, 11), contrato.DataUltimoReajuste);
            Assert.Equal(344.21m, await c.LinhaAsync(c.ManutencaoId));
            Assert.Equal(50m, await c.LinhaAsync(c.HelpDeskId));

            var historico = await c.Historico.ExecutarAsync(c.ContratoId, CancellationToken.None);
            var desfeito = historico.Single(e => e.Id == ontemId);
            Assert.Equal(StatusMovimentacaoValor.Desfeito, desfeito.Status);
            Assert.Equal("Índice de outubro saiu hoje", desfeito.MotivoDesfazer);
            Assert.NotNull(desfeito.DesfeitoEm);
            Assert.False(desfeito.PodeDesfazer);

            var novo = historico.Single(e => e.Tipo == TipoMovimentacaoValor.Reajuste && e.Status == StatusMovimentacaoValor.Aplicado);
            Assert.Equal(394.03m, desfeito.ValorNovo);
            Assert.Equal(378.98m, novo.ValorAnterior);
            Assert.Equal(394.21m, novo.ValorNovo);
            Assert.True(novo.PodeDesfazer);

            // "Somar" explícito continua possível.
            c.Db.ChangeTracker.Clear();
            await c.Registrar.ExecutarAsync(c.ContratoId,
                new RegistrarReajusteRequest(new DateOnly(2026, 11, 12), 400m, null, "acordo extra", ConfirmarReajusteRecente: true), CancellationToken.None);
            Assert.Equal(400m, (await c.ContratoAsync()).ValorAtual);
        });
    }

    [Fact]
    public async Task Desfazer_volta_valor_servicos_e_aniversario_e_fica_no_historico()
    {
        await ExecutarAsync(async c =>
        {
            var reajusteId = await c.Registrar.ExecutarAsync(c.ContratoId, ReajusteDeOntem(), CancellationToken.None);
            Assert.Equal(new DateOnly(2027, 11, 11),
                Application.Common.SituacaoContratoCalculator.CalcularDataProximoReajuste(await c.ContratoAsync()));

            // Motivo curto não passa.
            await Assert.ThrowsAsync<RegraNegocioException>(() => c.Desfazer.ExecutarAsync(c.ContratoId, reajusteId,
                new DesfazerReajusteRequest("errei"), CancellationToken.None));

            c.Db.ChangeTracker.Clear();
            await c.Desfazer.ExecutarAsync(c.ContratoId, reajusteId,
                new DesfazerReajusteRequest("Registrado antes de sair o IPCA de outubro"), CancellationToken.None);

            var contrato = await c.ContratoAsync();
            Assert.Equal(378.98m, contrato.ValorAtual);
            Assert.Equal(new DateOnly(2026, 11, 11), Application.Common.SituacaoContratoCalculator.CalcularDataProximoReajuste(contrato));
            Assert.Equal(328.98m, await c.LinhaAsync(c.ManutencaoId));
            Assert.Equal(50m, await c.LinhaAsync(c.HelpDeskId));

            var evento = (await c.Historico.ExecutarAsync(c.ContratoId, CancellationToken.None)).Single(e => e.Id == reajusteId);
            Assert.Equal(StatusMovimentacaoValor.Desfeito, evento.Status);
            Assert.False(evento.PodeDesfazer);
            Assert.Null(evento.BloqueioDesfazer);
            Assert.True(await c.Db.AuditLogs.AnyAsync(a => a.EntidadeId == c.ContratoId && a.Operacao == "DesfazerReajusteContrato"));

            // Desfazer de novo: já foi.
            c.Db.ChangeTracker.Clear();
            var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => c.Desfazer.ExecutarAsync(c.ContratoId, reajusteId,
                new DesfazerReajusteRequest("tentando de novo, por engano"), CancellationToken.None));
            Assert.Contains("já foi desfeito", erro.Message);

            // Depois de desfeito, registrar de novo no ciclo não pede escolha.
            c.Db.ChangeTracker.Clear();
            await c.Registrar.ExecutarAsync(c.ContratoId,
                new RegistrarReajusteRequest(new DateOnly(2026, 11, 11), 394.21m, IndiceReajusteContrato.Ipca, null), CancellationToken.None);
            Assert.Equal(394.21m, (await c.ContratoAsync()).ValorAtual);
        });
    }

    [Fact]
    public async Task So_o_ultimo_reajuste_e_sem_nota_de_producao_pode_ser_desfeito()
    {
        await ExecutarAsync(async c =>
        {
            var primeiroId = await c.Registrar.ExecutarAsync(c.ContratoId, ReajusteDeOntem(), CancellationToken.None);
            c.Db.ChangeTracker.Clear();
            var segundoId = await c.Registrar.ExecutarAsync(c.ContratoId,
                new RegistrarReajusteRequest(new DateOnly(2026, 11, 12), 400m, null, "acordo extra", ConfirmarReajusteRecente: true), CancellationToken.None);

            // O primeiro não: depois dele veio outro reajuste.
            c.Db.ChangeTracker.Clear();
            var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => c.Desfazer.ExecutarAsync(c.ContratoId, primeiroId,
                new DesfazerReajusteRequest("quero desfazer o primeiro"), CancellationToken.None));
            Assert.Contains("Só o último evento", erro.Message);

            // Nota de homologação não conta.
            await EmitirNotaAsync(c, TipoAmbiente.Homologacao);
            var historico = await c.Historico.ExecutarAsync(c.ContratoId, CancellationToken.None);
            Assert.True(historico.Single(e => e.Id == segundoId).PodeDesfazer);
            Assert.False(historico.Single(e => e.Id == primeiroId).PodeDesfazer);

            // Nota de produção com o valor novo: não desfaz mais, e a tela explica.
            await EmitirNotaAsync(c, TipoAmbiente.Producao);
            historico = await c.Historico.ExecutarAsync(c.ContratoId, CancellationToken.None);
            var segundo = historico.Single(e => e.Id == segundoId);
            Assert.False(segundo.PodeDesfazer);
            Assert.Contains("nota de produção", segundo.BloqueioDesfazer);

            c.Db.ChangeTracker.Clear();
            erro = await Assert.ThrowsAsync<RegraNegocioException>(() => c.Desfazer.ExecutarAsync(c.ContratoId, segundoId,
                new DesfazerReajusteRequest("quero desfazer o segundo"), CancellationToken.None));
            Assert.Contains("nota de produção", erro.Message);
            Assert.Equal(400m, (await c.ContratoAsync()).ValorAtual);
        });
    }

    private static async Task EmitirNotaAsync(Cenario c, TipoAmbiente ambiente)
    {
        c.Db.NotasFiscais.Add(new Nfse
        {
            EmpresaId = c.EmpresaId, ClienteId = c.ClienteId, ServicoId = c.ManutencaoId, ContratoId = c.ContratoId,
            NumeroDps = 900_000 + Random.Shared.Next(99_999), SerieDps = "900", NumeroNfse = "123",
            ChaveAcesso = "CHAVE" + Guid.NewGuid().ToString("N"), DataCompetencia = new DateOnly(2026, 11, 1),
            DataEmissao = DateTimeOffset.UtcNow, ValorServico = 400m, DescricaoServico = "Manutenção",
            CodigoTributacaoNacional = "010501", CodigoNbs = "115013000", TribIssqn = "1", TpRetIssqn = "1",
            CstPisCofins = "00", TpRetPisCofins = "1", Status = NfseStatus.Autorizada, TipoAmbiente = ambiente,
            XmlNfse = "<NFSe xmlns=\"http://www.sped.fazenda.gov.br/nfse\"><infNFSe Id=\"NFS123\"/></NFSe>"
        });
        await c.Db.SaveChangesAsync();
        c.Db.ChangeTracker.Clear();
    }
}
