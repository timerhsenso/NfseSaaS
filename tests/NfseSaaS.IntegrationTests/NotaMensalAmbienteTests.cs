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
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using NfseSaaS.Nacional.Models;
using NfseSaaS.Nacional.Options;
using NfseSaaS.Nacional.Responses;
using NfseSaaS.Nacional.Services;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// Homologação é homologação, produção é produção: a Nota Mensal testada em
/// homologação não marca o mês de produção como emitido, não vira "última
/// nota" e não trava a emissão de verdade (chave de idempotência por ambiente).
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class NotaMensalAmbienteTests
{
    private readonly AplicacaoTesteFactory _factory;

    public NotaMensalAmbienteTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class SefinFalsa : INfseNacionalService
    {
        public List<DpsRequest> Enviadas { get; } = new();

        public Task<NfseNacionalResponse> EmitirAsync(DpsRequest request, Guid empresaId, CancellationToken cancellationToken)
        {
            Enviadas.Add(request);
            return Task.FromResult(new NfseNacionalResponse(true, int.Parse(request.TpAmb), "teste", DateTimeOffset.UtcNow, "DPS" + request.NumeroDps,
                $"2919207{request.TpAmb}2{request.NumeroDps:D41}", null, Array.Empty<NfseErro>()));
        }

        public Task<EventoNacionalResponse> CancelarAsync(EventoCancelamentoRequest request, Guid empresaId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CobrancaLivre : IVerificadorCobranca
    {
        public Task GarantirPodeEmitirEmProducaoAsync(Guid tenantId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task Nota_de_homologacao_nao_conta_na_producao()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Ambiente " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        try
        {
            // Produção exige e-mail da conta confirmado.
            var grupo = new Grupo { Nome = "Admin " + s, EhAdministrador = true };
            db.Grupos.Add(grupo);
            await db.SaveChangesAsync();
            db.Users.Add(new ApplicationUser { TenantId = conta.Id, UserName = $"adm{s}@teste.com", Email = $"adm{s}@teste.com", EmailConfirmed = true, GrupoId = grupo.Id });

            var empresa = new Empresa
            {
                Cnpj = "04747304000178", RazaoSocial = "Empresa " + s, InscricaoMunicipal = "12345",
                CodigoMunicipio = "2919207", Uf = "BA", Cep = "42700000", Logradouro = "Rua A", Numero = "1", Bairro = "Centro",
                Telefone = "71999999999", Email = "empresa@teste.com", TipoAmbiente = TipoAmbiente.Homologacao,
                OpSimpNac = "3", RegApTribSN = "1", RegEspTrib = "0", TribIssqn = "1", CstPisCofins = "08", TpRetPisCofins = "0",
                PercentualTotalTributosSimplesNacional = "6.00"
            };
            db.Empresas.Add(empresa);
            await db.SaveChangesAsync();
            var cliente = new Cliente
            {
                EmpresaId = empresa.Id, CpfCnpj = "52998224725", Nome = "Holtz", Apelido = "HOLTZ " + s[..6],
                Cep = "42700000", Logradouro = "Rua B", Numero = "2", Bairro = "Centro", CodigoMunicipio = "2919207", Uf = "BA"
            };
            var manutencao = new Servico { EmpresaId = empresa.Id, Descricao = "Manutenção do sistema", CodigoTributacaoNacional = "010501", CodigoNbs = "115013000" };
            db.Clientes.Add(cliente);
            db.Servicos.Add(manutencao);
            await db.SaveChangesAsync();

            var audit = sp.GetRequiredService<IAuditLogWriter>();
            var contratoId = await new CadastrarContratoUseCase(db, audit).ExecutarAsync(new CadastrarContratoRequest(
                empresa.Id, cliente.Id, "Suporte mensal",
                new[] { new ContratoServicoRequest(manutencao.Id, 1, 100) },
                new DateOnly(2026, 1, 1), 12, null, null, null, StatusContrato.Ativo, null, TipoCobrancaContrato.Mensal,
                PermitirAlterarValorNaEmissao: true), CancellationToken.None);

            var sefin = new SefinFalsa();
            var emitirNfse = new EmitirNfseUseCase(db, sefin, audit, sp.GetRequiredService<INfseEventoWriter>(),
                sp.GetRequiredService<IEnviarNfsePorEmailUseCase>(), NullLogger<EmitirNfseUseCase>.Instance, new CobrancaLivre(),
                sp.GetRequiredService<IOptions<NfseNacionalOptions>>());
            var lote = new EmitirNotaMensalLoteUseCase(db, emitirNfse, audit);
            var listar = new ListarCandidatosNotaMensalUseCase(db);
            var outubro = new DateOnly(2026, 10, 1);
            var novembro = new DateOnly(2026, 11, 1);

            async Task<ItemResultadoNotaMensalResponse> Emitir(DateOnly competencia, decimal? total = null) =>
                Assert.Single(await lote.ExecutarAsync(new EmitirNotaMensalLoteRequest(empresa.Id, competencia, new[]
                {
                    new EmitirNotaMensalItemRequest(contratoId, total)
                }), CancellationToken.None));

            async Task MudarAmbiente(TipoAmbiente ambiente)
            {
                await db.Empresas.Where(e => e.Id == empresa.Id).ExecuteUpdateAsync(x => x.SetProperty(e => e.TipoAmbiente, ambiente));
                db.ChangeTracker.Clear();
            }

            // Testes em homologação: outubro com valor diferente e novembro.
            Assert.True((await Emitir(outubro, 700m)).Sucesso);
            var novembroHomologacao = await Emitir(novembro);
            Assert.True(novembroHomologacao.Sucesso, novembroHomologacao.Mensagem);

            var emHomologacao = Assert.Single(await listar.ExecutarAsync(empresa.Id, novembro, CancellationToken.None));
            Assert.True(emHomologacao.JaEmitidoNestaCompetencia);
            Assert.Equal(700m, emHomologacao.UltimoTotal);

            // Vai para produção: o mês está em aberto e nada de homologação vira sugestão.
            await MudarAmbiente(TipoAmbiente.Producao);
            var emProducao = Assert.Single(await listar.ExecutarAsync(empresa.Id, novembro, CancellationToken.None));
            Assert.False(emProducao.JaEmitidoNestaCompetencia);
            Assert.Null(emProducao.UltimoTotal);
            Assert.All(emProducao.Linhas, l => Assert.Null(l.UltimoValor));

            // Emite de verdade (mesma chave do lote, outro ambiente).
            var enviadasAntes = sefin.Enviadas.Count;
            var novembroProducao = await Emitir(novembro);
            Assert.True(novembroProducao.Sucesso, novembroProducao.Mensagem);
            Assert.NotEqual(novembroHomologacao.NfseId, novembroProducao.NfseId);
            Assert.Equal(enviadasAntes + 1, sefin.Enviadas.Count);
            var notaProducao = await db.NotasFiscais.AsNoTracking().SingleAsync(n => n.Id == novembroProducao.NfseId);
            Assert.Equal(TipoAmbiente.Producao, notaProducao.TipoAmbiente);

            // Agora sim está emitido em produção, e clicar de novo não duplica.
            Assert.True(Assert.Single(await listar.ExecutarAsync(empresa.Id, novembro, CancellationToken.None)).JaEmitidoNestaCompetencia);
            var deNovo = await Emitir(novembro);
            Assert.Equal(novembroProducao.NfseId, deNovo.NfseId);
            Assert.Equal(enviadasAntes + 1, sefin.Enviadas.Count);

            // De volta à homologação: lá continua o teste de antes.
            await MudarAmbiente(TipoAmbiente.Homologacao);
            var deVolta = Assert.Single(await listar.ExecutarAsync(empresa.Id, novembro, CancellationToken.None));
            Assert.True(deVolta.JaEmitidoNestaCompetencia);
            Assert.Equal(700m, deVolta.UltimoTotal);
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
            await db.Users.Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Grupos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }
}
