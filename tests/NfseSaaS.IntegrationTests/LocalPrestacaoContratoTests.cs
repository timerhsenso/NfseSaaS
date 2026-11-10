using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Application.UseCases.Municipios;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>C2 com o banco real: local da prestação padrão do Contrato (gravar, conferir no IBGE, limpar).</summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class LocalPrestacaoContratoTests
{
    private readonly AplicacaoTesteFactory _factory;

    public LocalPrestacaoContratoTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Contrato_guarda_o_local_confere_o_codigo_e_volta_para_o_da_empresa()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Local " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        try
        {
            var empresa = new Empresa { Cnpj = s[..14], RazaoSocial = "Empresa " + s, CodigoMunicipio = "2919207" };
            db.Empresas.Add(empresa);
            await db.SaveChangesAsync();
            var cliente = new Cliente { EmpresaId = empresa.Id, CpfCnpj = "52998224725", Nome = "Cliente", Apelido = "Cli " + s[..6] };
            var servico = new Servico { EmpresaId = empresa.Id, Descricao = "Suporte" };
            db.Clientes.Add(cliente);
            db.Servicos.Add(servico);
            await db.SaveChangesAsync();

            var audit = sp.GetRequiredService<IAuditLogWriter>();
            var cadastrar = new CadastrarContratoUseCase(db, audit);
            CadastrarContratoRequest Pedido(string? local) => new(
                empresa.Id, cliente.Id, "Mensal", new[] { new ContratoServicoRequest(servico.Id, 1, 100) },
                new DateOnly(2026, 11, 1), 12, null, null, null, StatusContrato.Ativo, null, TipoCobrancaContrato.Mensal, true,
                CodigoMunicipioPrestacao: local);

            var emSalvador = await cadastrar.ExecutarAsync(Pedido("2927408"), CancellationToken.None);
            Assert.Equal("2927408", (await db.Contratos.AsNoTracking().SingleAsync(c => c.Id == emSalvador)).CodigoMunicipioPrestacao);

            // O mesmo município da empresa não é guardado à parte.
            var daEmpresa = await cadastrar.ExecutarAsync(Pedido("2919207"), CancellationToken.None);
            Assert.Null((await db.Contratos.AsNoTracking().SingleAsync(c => c.Id == daEmpresa)).CodigoMunicipioPrestacao);

            var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => cadastrar.ExecutarAsync(Pedido("2999999"), CancellationToken.None));
            Assert.Equal(RegrasMunicipio.MensagemNaoExiste("2999999"), erro.Message);

            // Edição: nulo não mexe; em branco volta para o da empresa.
            var atualizar = new AtualizarContratoUseCase(db, audit);
            AtualizarContratoRequest Edicao(string? local) => new(
                "Mensal", new DateOnly(2026, 11, 1), 12, null, null, null, StatusContrato.Ativo, null, TipoCobrancaContrato.Mensal, true,
                CodigoMunicipioPrestacao: local);

            await atualizar.ExecutarAsync(emSalvador, Edicao(null), CancellationToken.None);
            Assert.Equal("2927408", (await db.Contratos.AsNoTracking().SingleAsync(c => c.Id == emSalvador)).CodigoMunicipioPrestacao);

            await atualizar.ExecutarAsync(emSalvador, Edicao(""), CancellationToken.None);
            Assert.Null((await db.Contratos.AsNoTracking().SingleAsync(c => c.Id == emSalvador)).CodigoMunicipioPrestacao);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.MovimentacoesValorContrato.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.ContratoServicos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Contratos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Servicos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Clientes.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }
}
