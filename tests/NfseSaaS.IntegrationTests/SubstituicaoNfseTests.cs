using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using NfseSaaS.Nacional.Models;
using NfseSaaS.Nacional.Options;
using NfseSaaS.Nacional.Responses;
using NfseSaaS.Nacional.Services;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// C7 com o banco real e a SEFIN falsa: a substituta leva o grupo subst
/// (chave da original), a original vira Substituida só quando a substituta
/// é autorizada, o Simples não muda o valor e só há uma substituta por nota.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class SubstituicaoNfseTests
{
    private readonly AplicacaoTesteFactory _factory;

    public SubstituicaoNfseTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class SefinFalsa : INfseNacionalService
    {
        public List<DpsRequest> Enviadas { get; } = new();
        public bool Rejeitar { get; set; }

        public Task<NfseNacionalResponse> EmitirAsync(DpsRequest request, Guid empresaId, CancellationToken cancellationToken)
        {
            Enviadas.Add(request);
            var resposta = Rejeitar
                ? new NfseNacionalResponse(false, 2, "teste", DateTimeOffset.UtcNow, null, null, null, new[] { new NfseErro("E0050", "Fora do prazo (teste).") })
                : new NfseNacionalResponse(true, 2, "teste", DateTimeOffset.UtcNow, "DPS" + request.NumeroDps,
                    $"2919207220{request.NumeroDps:D40}", null, Array.Empty<NfseErro>());
            return Task.FromResult(resposta);
        }

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

    [Fact]
    public async Task Substituta_leva_a_chave_da_original_e_a_original_vira_substituida()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Subst " + s };
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
                OpSimpNac = "3", RegApTribSN = "1", RegEspTrib = "0", TribIssqn = "1", CstPisCofins = "08",
                PercentualTotalTributosSimplesNacional = "6.00"
            };
            db.Empresas.Add(empresa);
            await db.SaveChangesAsync();
            var cliente = new Cliente
            {
                EmpresaId = empresa.Id, CpfCnpj = "52998224725", Nome = "Cliente Teste", Apelido = "Cli " + s[..6],
                Cep = "42700000", Logradouro = "Rua B", Numero = "2", Bairro = "Centro", CodigoMunicipio = "2919207", Uf = "BA"
            };
            var servico = new Servico { EmpresaId = empresa.Id, Descricao = "Suporte", CodigoTributacaoNacional = "010501", CodigoNbs = "115013000" };
            db.Clientes.Add(cliente);
            db.Servicos.Add(servico);
            await db.SaveChangesAsync();

            var sefin = new SefinFalsa();
            var emitir = Emissao(sp, sefin);
            var pedido = new EmitirNfseRequest(empresa.Id, cliente.Id, servico.Id, 100m, "Suporte de outubro", new DateOnly(2026, 11, 1));

            var original = await emitir.ExecutarAsync(pedido, CancellationToken.None);
            Assert.True(original.Sucesso);
            var chaveOriginal = original.ChaveAcesso!;

            // Simples Nacional: valor não muda na substituição (E0061) — barrado antes da SEFIN.
            var enviadasAntes = sefin.Enviadas.Count;
            var travado = await Assert.ThrowsAsync<RegraNegocioException>(() => emitir.ExecutarAsync(
                pedido with { ValorServico = 120m, SubstituirNfseId = original.NfseId, MotivoSubstituicao = "05" }, CancellationToken.None));
            Assert.Contains("valor do serviço", travado.Message);
            Assert.Equal(enviadasAntes, sefin.Enviadas.Count);

            // Motivo 99 sem descrição.
            await Assert.ThrowsAsync<RegraNegocioException>(() => emitir.ExecutarAsync(
                pedido with { SubstituirNfseId = original.NfseId, MotivoSubstituicao = "99" }, CancellationToken.None));

            // SEFIN rejeita: a original continua autorizada e dá para tentar de novo.
            sefin.Rejeitar = true;
            var rejeitada = await emitir.ExecutarAsync(pedido with
            {
                DescricaoServico = "Suporte de outubro (corrigido)", SubstituirNfseId = original.NfseId,
                MotivoSubstituicao = "99", DescricaoMotivoSubstituicao = "Descrição do serviço corrigida"
            }, CancellationToken.None);
            Assert.False(rejeitada.Sucesso);
            Assert.Equal(NfseStatus.Autorizada, (await db.NotasFiscais.AsNoTracking().SingleAsync(n => n.Id == original.NfseId)).Status);

            sefin.Rejeitar = false;
            var substituta = await emitir.ExecutarAsync(pedido with
            {
                DescricaoServico = "Suporte de outubro (corrigido)", SubstituirNfseId = original.NfseId,
                MotivoSubstituicao = "99", DescricaoMotivoSubstituicao = "Descrição do serviço corrigida"
            }, CancellationToken.None);
            Assert.True(substituta.Sucesso);

            var enviada = sefin.Enviadas[^1];
            Assert.Equal(new SubstituicaoDps(chaveOriginal, "99", "Descrição do serviço corrigida"), enviada.Substituicao);

            db.ChangeTracker.Clear();
            var notaOriginal = await db.NotasFiscais.AsNoTracking().SingleAsync(n => n.Id == original.NfseId);
            Assert.Equal(NfseStatus.Substituida, notaOriginal.Status);
            Assert.True(await db.NfseEventos.AnyAsync(e => e.NfseId == original.NfseId && e.Tipo == NfseEventoTipo.Substituida));

            var nova = await new ObterNfsePorIdUseCase(db).ExecutarAsync(substituta.NfseId, CancellationToken.None);
            Assert.Equal(original.NfseId, nova.SubstituiNfseId);
            Assert.True(nova.PodeSubstituir);
            var antiga = await new ObterNfsePorIdUseCase(db).ExecutarAsync(original.NfseId, CancellationToken.None);
            Assert.Equal(nova.NumeroDps, antiga.SubstituidaPorNumeroDps);
            Assert.False(antiga.PodeSubstituir);

            // Original já substituída não aceita outra substituta.
            await Assert.ThrowsAsync<RegraNegocioException>(() => emitir.ExecutarAsync(
                pedido with { SubstituirNfseId = original.NfseId, MotivoSubstituicao = "05" }, CancellationToken.None));

            var dados = await new ObterDadosSubstituicaoUseCase(db).ExecutarAsync(original.NfseId, CancellationToken.None);
            Assert.NotNull(dados.Impedimento);
            var dadosNova = await new ObterDadosSubstituicaoUseCase(db).ExecutarAsync(substituta.NfseId, CancellationToken.None);
            Assert.Null(dadosNova.Impedimento);
            Assert.Contains(CampoTravadoSubstituicao.Valor, dadosNova.CamposTravados);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.NfseEventos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.NotasFiscais.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id && x.SubstituiNfseId != null).ExecuteDeleteAsync();
            await db.NotasFiscais.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Clientes.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Servicos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM contadores_dps WHERE \"EmpresaId\" IN (SELECT \"Id\" FROM empresas WHERE \"TenantId\" = {conta.Id})");
            await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }
}
