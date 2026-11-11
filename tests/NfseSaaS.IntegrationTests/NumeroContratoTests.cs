using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.Persistence.Seeders;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// Número do contrato: proposto "ano/NNNN" por empresa, texto livre quando
/// digitado, único por empresa (sem diferenciar maiúsculas), e os contratos
/// antigos numerados na subida pela data de início.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class NumeroContratoTests
{
    private readonly AplicacaoTesteFactory _factory;

    public NumeroContratoTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Numera_aceita_texto_livre_barra_repetido_e_preenche_os_antigos()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Numero contrato " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        try
        {
            var empresa = new Empresa { Cnpj = s[..14], RazaoSocial = "Empresa " + s };
            var outra = new Empresa { Cnpj = s[14..28], RazaoSocial = "Outra " + s };
            db.Empresas.AddRange(empresa, outra);
            await db.SaveChangesAsync();
            var cliente = new Cliente { EmpresaId = null, Nome = "Cliente", Apelido = "CLI" + s[..6], CpfCnpj = "52998224725" };
            var servico = new Servico { EmpresaId = empresa.Id, Descricao = "Manutenção", CodigoTributacaoNacional = "010501", CodigoNbs = "115013000" };
            var servicoOutra = new Servico { EmpresaId = outra.Id, Descricao = "Manutenção", CodigoTributacaoNacional = "010501", CodigoNbs = "115013000" };
            db.Clientes.Add(cliente);
            db.Servicos.AddRange(servico, servicoOutra);
            await db.SaveChangesAsync();

            var audit = sp.GetRequiredService<IAuditLogWriter>();
            var cadastrar = new CadastrarContratoUseCase(db, audit);
            var proximo = new ObterProximoNumeroContratoUseCase(db);
            var ano = DataBrasilia.Hoje().Year;

            Task<Guid> Novo(Guid empresaId, Guid servicoId, string? numero, string? descricao = null, DateOnly? inicio = null) =>
                cadastrar.ExecutarAsync(new CadastrarContratoRequest(
                    empresaId, cliente.Id, descricao, new[] { new ContratoServicoRequest(servicoId, 1, 100m) },
                    inicio ?? new DateOnly(ano, 1, 1), 12, null, null, null, StatusContrato.Ativo, null,
                    TipoCobrancaContrato.Mensal, PermitirAlterarValorNaEmissao: false, Numero: numero), CancellationToken.None);
            async Task<string?> NumeroDe(Guid id) => (await db.Contratos.AsNoTracking().SingleAsync(c => c.Id == id)).Numero;

            // Sem número: o próximo da empresa; a tela mostra o mesmo antes.
            Assert.Equal($"{ano}/0001", await proximo.ExecutarAsync(empresa.Id, CancellationToken.None));
            var primeiro = await Novo(empresa.Id, servico.Id, null);
            Assert.Equal($"{ano}/0001", await NumeroDe(primeiro));
            Assert.Equal($"{ano}/0002", await proximo.ExecutarAsync(empresa.Id, CancellationToken.None));

            // Texto livre, descrição opcional; o padrão continua depois do maior.
            var livre = await Novo(empresa.Id, servico.Id, "  CT-15 ");
            Assert.Equal("CT-15", await NumeroDe(livre));
            await Novo(empresa.Id, servico.Id, $"{ano}/0010", "Folha filial");
            Assert.Equal($"{ano}/0011", await proximo.ExecutarAsync(empresa.Id, CancellationToken.None));

            // Repetido na mesma empresa (sem diferenciar maiúsculas) é barrado; em outra empresa pode.
            await Assert.ThrowsAsync<RegraNegocioException>(() => Novo(empresa.Id, servico.Id, "ct-15"));
            var naOutra = await Novo(outra.Id, servicoOutra.Id, "CT-15");
            Assert.Equal("CT-15", await NumeroDe(naOutra));

            // Editar: troca o número; nulo não mexe; repetido é barrado.
            var atualizar = new AtualizarContratoUseCase(db, audit);
            AtualizarContratoRequest Edicao(string? numero) => new(
                "", new DateOnly(ano, 1, 1), 12, null, null, null, StatusContrato.Ativo, null, TipoCobrancaContrato.Mensal, false, Numero: numero);
            await atualizar.ExecutarAsync(primeiro, Edicao("A-1"), CancellationToken.None);
            db.ChangeTracker.Clear();
            Assert.Equal("A-1", await NumeroDe(primeiro));
            await atualizar.ExecutarAsync(primeiro, Edicao(null), CancellationToken.None);
            db.ChangeTracker.Clear();
            Assert.Equal("A-1", await NumeroDe(primeiro));
            await Assert.ThrowsAsync<RegraNegocioException>(() => atualizar.ExecutarAsync(primeiro, Edicao("CT-15"), CancellationToken.None));
            db.ChangeTracker.Clear();

            // Contratos antigos (sem número): numerados na subida pela data de início.
            var antigo2019 = await Novo(empresa.Id, servico.Id, "X1", inicio: new DateOnly(2019, 5, 1));
            var antigo2019b = await Novo(empresa.Id, servico.Id, "X2", inicio: new DateOnly(2019, 2, 1));
            var antigo2020 = await Novo(empresa.Id, servico.Id, "X3", inicio: new DateOnly(2020, 3, 1));
            await db.Contratos.Where(c => c.Id == antigo2019 || c.Id == antigo2019b || c.Id == antigo2020)
                .ExecuteUpdateAsync(x => x.SetProperty(c => c.Numero, (string?)null));
            await NumeroContratoBackfill.PreencherAsync(_factory.Services);
            await NumeroContratoBackfill.PreencherAsync(_factory.Services); // idempotente
            db.ChangeTracker.Clear();
            Assert.Equal("2019/0001", await NumeroDe(antigo2019b));
            Assert.Equal("2019/0002", await NumeroDe(antigo2019));
            Assert.Equal("2020/0001", await NumeroDe(antigo2020));

            // A busca da lista encontra pelo número.
            var lista = await new ListarContratosUseCase(db).ExecutarAsync(
                new ListarContratosRequest(empresa.Id, Busca: "CT-15", IncluirInativos: true), CancellationToken.None);
            Assert.Equal(livre, Assert.Single(lista.Items).Id);
            Assert.Equal("CT-15", lista.Items[0].Numero);
        }
        finally
        {
            db.ChangeTracker.Clear();
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
