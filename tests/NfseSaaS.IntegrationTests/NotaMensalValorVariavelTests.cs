using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Application.UseCases.NotaMensal;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Contratos;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using NfseSaaS.Nacional.Models;
using NfseSaaS.Nacional.Options;
using NfseSaaS.Nacional.Responses;
using NfseSaaS.Nacional.Services;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// Nota Mensal com valor por serviço, com o banco real e a SEFIN falsa:
/// manutenção fixa (cadastrada errada com R$ 1) corrigida pelo "gravar no
/// contrato", HelpDesk variável sugerido pelo último valor no mês seguinte.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class NotaMensalValorVariavelTests
{
    private readonly AplicacaoTesteFactory _factory;

    public NotaMensalValorVariavelTests(AplicacaoTesteFactory factory)
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
                $"2919207220{request.NumeroDps:D40}", null, Array.Empty<NfseErro>()));
        }

        public Task<EventoNacionalResponse> CancelarAsync(EventoCancelamentoRequest request, Guid empresaId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CobrancaLivre : IVerificadorCobranca
    {
        public Task GarantirPodeEmitirEmProducaoAsync(Guid tenantId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task Grava_o_fixo_no_contrato_e_sugere_o_ultimo_valor_do_variavel()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Variavel " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        try
        {
            var empresa = new Empresa
            {
                Cnpj = "04747304000178", RazaoSocial = "Empresa " + s, InscricaoMunicipal = "12345",
                CodigoMunicipio = "2919207", Uf = "BA", Cep = "42700000", Logradouro = "Rua A", Numero = "1", Bairro = "Centro",
                Telefone = "71999999999", Email = "empresa@teste.com",
                OpSimpNac = "3", RegApTribSN = "1", RegEspTrib = "0", TribIssqn = "1", CstPisCofins = "08", TpRetPisCofins = "0",
                PercentualTotalTributosSimplesNacional = "6.00"
            };
            db.Empresas.Add(empresa);
            await db.SaveChangesAsync();
            var cliente = new Cliente
            {
                EmpresaId = empresa.Id, CpfCnpj = "52998224725", Nome = "Cliente Teste", Apelido = "Cli " + s[..6],
                Cep = "42700000", Logradouro = "Rua B", Numero = "2", Bairro = "Centro", CodigoMunicipio = "2919207", Uf = "BA"
            };
            var manutencao = new Servico { EmpresaId = empresa.Id, Descricao = "Manutenção do sistema", CodigoTributacaoNacional = "010501", CodigoNbs = "115013000" };
            var helpDesk = new Servico { EmpresaId = empresa.Id, Descricao = "HelpDesk", CodigoTributacaoNacional = "010501", CodigoNbs = "115013000" };
            db.Clientes.Add(cliente);
            db.Servicos.AddRange(manutencao, helpDesk);
            await db.SaveChangesAsync();

            var audit = sp.GetRequiredService<IAuditLogWriter>();
            var contratoId = await new CadastrarContratoUseCase(db, audit).ExecutarAsync(new CadastrarContratoRequest(
                empresa.Id, cliente.Id, "Suporte mensal",
                new[] { new ContratoServicoRequest(manutencao.Id, 1, 1), new ContratoServicoRequest(helpDesk.Id, 1, 1, ValorVariavel: true) },
                new DateOnly(2026, 1, 1), 12, null, null, null, StatusContrato.Ativo, null, TipoCobrancaContrato.Mensal,
                PermitirAlterarValorNaEmissao: false), CancellationToken.None);
            var linhas = await db.ContratoServicos.AsNoTracking().Where(cs => cs.ContratoId == contratoId).ToListAsync();
            var linhaManutencao = linhas.Single(l => l.ServicoId == manutencao.Id).Id;
            var linhaHelpDesk = linhas.Single(l => l.ServicoId == helpDesk.Id).Id;
            Assert.True(linhas.Single(l => l.Id == linhaHelpDesk).ValorVariavel);

            var sefin = new SefinFalsa();
            var emitirNfse = new EmitirNfseUseCase(db, sefin, audit, sp.GetRequiredService<INfseEventoWriter>(),
                sp.GetRequiredService<IEnviarNfsePorEmailUseCase>(), NullLogger<EmitirNfseUseCase>.Instance, new CobrancaLivre(),
                sp.GetRequiredService<IOptions<NfseNacionalOptions>>());
            var lote = new EmitirNotaMensalLoteUseCase(db, emitirNfse, audit);
            var listar = new ListarCandidatosNotaMensalUseCase(db);

            // Outubro: fixo mudou sem "gravar" num contrato que não permite → recusado.
            var outubro = new DateOnly(2026, 10, 1);
            var recusado = await lote.ExecutarAsync(new EmitirNotaMensalLoteRequest(empresa.Id, outubro, new[]
            {
                new EmitirNotaMensalItemRequest(contratoId, null, new[] { new ValorServicoNotaMensalRequest(linhaManutencao, 1500m) })
            }), CancellationToken.None);
            Assert.False(Assert.Single(recusado).Sucesso);
            Assert.Empty(sefin.Enviadas);

            // Outubro: grava a manutenção no contrato e informa o HelpDesk.
            var resultados = await lote.ExecutarAsync(new EmitirNotaMensalLoteRequest(empresa.Id, outubro, new[]
            {
                new EmitirNotaMensalItemRequest(contratoId, null, new[]
                {
                    new ValorServicoNotaMensalRequest(linhaManutencao, 1500m, GravarNoContrato: true),
                    new ValorServicoNotaMensalRequest(linhaHelpDesk, 800m)
                })
            }), CancellationToken.None);
            var r = Assert.Single(resultados);
            Assert.True(r.Sucesso, r.Mensagem);
            Assert.True(r.ContratoAtualizado);
            Assert.Equal(2300m, Assert.Single(sefin.Enviadas).Valor);

            db.ChangeTracker.Clear();
            var contrato = await db.Contratos.AsNoTracking().SingleAsync(c => c.Id == contratoId);
            Assert.Equal(1501m, contrato.ValorAtual);
            var movimento = await db.MovimentacoesValorContrato.AsNoTracking()
                .SingleAsync(m => m.ContratoId == contratoId && m.Tipo == TipoMovimentacaoValor.Alteracao);
            Assert.Equal(2m, movimento.ValorAnterior);
            Assert.Equal(new DateOnly(2026, 10, 1), movimento.DataVigencia);

            var nota = await db.NotasFiscais.AsNoTracking().SingleAsync(n => n.Id == r.NfseId);
            var composicao = ComposicaoValoresNfse.Desserializar(nota.ComposicaoValoresJson);
            Assert.Equal(800m, composicao.Single(i => i.ContratoServicoId == linhaHelpDesk).Valor);

            // Novembro: o fixo vem do contrato; o variável sugere o último valor.
            var candidato = Assert.Single(await listar.ExecutarAsync(empresa.Id, new DateOnly(2026, 11, 1), CancellationToken.None));
            var manutencaoNov = candidato.Linhas.Single(l => l.ContratoServicoId == linhaManutencao);
            var helpDeskNov = candidato.Linhas.Single(l => l.ContratoServicoId == linhaHelpDesk);
            Assert.Equal(1500m, manutencaoNov.ValorSugerido);
            Assert.False(manutencaoNov.ValorVariavel);
            Assert.True(helpDeskNov.ValorVariavel);
            Assert.Equal(800m, helpDeskNov.UltimoValor);
            Assert.Equal(outubro, helpDeskNov.UltimaCompetencia);
            Assert.Equal(800m, helpDeskNov.ValorSugerido);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.NfseEventos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.NotasFiscais.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.MovimentacoesValorContrato.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.ContratoServicos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Contratos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Clientes.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Servicos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM contadores_dps WHERE \"EmpresaId\" IN (SELECT \"Id\" FROM empresas WHERE \"TenantId\" = {conta.Id})");
            await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }
}
