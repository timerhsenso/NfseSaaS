using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// C10 com o banco real: cliente compartilhado entre as empresas da conta
/// (EmpresaId nulo), exclusivo vencendo o compartilhado, padrões por perfil,
/// unicidade (regra e índices parciais) e isolamento entre contas.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class ClienteCompartilhadoTests
{
    private readonly AplicacaoTesteFactory _factory;

    public ClienteCompartilhadoTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed record Cenario(AppDbContext Db, IServiceProvider Sp, Tenant Conta, Empresa Matriz, Empresa Filial, string S);

    private static async Task<Cenario> MontarAsync(IServiceProvider sp, PerfilConta perfil = PerfilConta.Empresa, bool comFilial = true)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Compartilhado " + s, Perfil = perfil };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        var matriz = new Empresa { Cnpj = s[..14], RazaoSocial = "Matriz " + s };
        var filial = new Empresa { Cnpj = s[1..15], RazaoSocial = "Filial " + s, Ativo = comFilial };
        db.Empresas.AddRange(matriz, filial);
        await db.SaveChangesAsync();
        return new Cenario(db, sp, conta, matriz, filial, s);
    }

    private static async Task LimparAsync(AppDbContext db, Guid tenantId)
    {
        db.ChangeTracker.Clear();
        await db.ContratoServicos.IgnoreQueryFilters().Where(x => db.Contratos.IgnoreQueryFilters().Any(c => c.Id == x.ContratoId && c.TenantId == tenantId)).ExecuteDeleteAsync();
        await db.MovimentacoesValorContrato.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Contratos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Servicos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Clientes.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Tenants.Where(x => x.Id == tenantId).ExecuteDeleteAsync();
    }

    private static CadastrarClienteRequest Pedido(Guid empresaId, string documento, string apelido, bool? compartilhado) =>
        new(empresaId, documento, "Cliente " + apelido, apelido, null, null, null, null, null, null, null, null, null,
            Compartilhado: compartilhado);

    private static AtualizarClienteRequest Edicao(string apelido, bool? compartilhado, Guid? empresaId) =>
        new("Cliente " + apelido, apelido, null, null, null, null, null, null, null, null, null,
            Compartilhado: compartilhado, EmpresaId: empresaId);

    private static CadastrarClienteUseCase Cadastrar(Cenario c) =>
        new(c.Db, c.Sp.GetRequiredService<IAuditLogWriter>(), c.Sp.GetRequiredService<ICurrentTenant>(),
            c.Sp.GetRequiredService<NfseSaaS.Application.UseCases.Recursos.IRecursosConta>());

    private static AtualizarClienteUseCase Atualizar(Cenario c) =>
        new(c.Db, c.Sp.GetRequiredService<IAuditLogWriter>());

    private static async Task<List<ClienteResponse>> ListarAsync(Cenario c, Guid empresaId) =>
        (await new ListarClientesUseCase(c.Db).ExecutarAsync(new ListarClientesRequest(empresaId, PageSize: 100, IncluirInativos: true), CancellationToken.None))
            .Items.ToList();

    [Fact]
    public async Task Lista_traz_compartilhados_e_exclusivos_e_o_exclusivo_vence()
    {
        using var escopo = _factory.Services.CreateScope();
        var c = await MontarAsync(escopo.ServiceProvider);
        try
        {
            var docX = "1" + c.S[..10];
            var docY = "2" + c.S[..10];
            var cadastrar = Cadastrar(c);

            var compartilhadoX = await cadastrar.ExecutarAsync(Pedido(c.Matriz.Id, docX, "X geral", compartilhado: true), CancellationToken.None);
            var exclusivoXFilial = await cadastrar.ExecutarAsync(Pedido(c.Filial.Id, docX, "X filial", compartilhado: false), CancellationToken.None);
            var exclusivoYMatriz = await cadastrar.ExecutarAsync(Pedido(c.Matriz.Id, docY, "Y matriz", compartilhado: false), CancellationToken.None);

            var matriz = await ListarAsync(c, c.Matriz.Id);
            Assert.Equal(new[] { compartilhadoX, exclusivoYMatriz }.OrderBy(x => x), matriz.Select(x => x.Id).OrderBy(x => x));
            Assert.True(matriz.Single(x => x.Id == compartilhadoX).Compartilhado);
            Assert.Null(matriz.Single(x => x.Id == compartilhadoX).EmpresaId);

            // Na filial, o exclusivo com o mesmo documento esconde o compartilhado.
            var filial = await ListarAsync(c, c.Filial.Id);
            Assert.Equal(exclusivoXFilial, Assert.Single(filial).Id);
            Assert.False(filial[0].Compartilhado);
        }
        finally
        {
            await LimparAsync(c.Db, c.Conta.Id);
        }
    }

    [Fact]
    public async Task Contrato_aceita_compartilhado_e_recusa_exclusivo_de_outra_empresa()
    {
        using var escopo = _factory.Services.CreateScope();
        var c = await MontarAsync(escopo.ServiceProvider);
        try
        {
            var cadastrar = Cadastrar(c);
            var compartilhado = await cadastrar.ExecutarAsync(Pedido(c.Matriz.Id, "3" + c.S[..10], "Geral", true), CancellationToken.None);
            var exclusivoMatriz = await cadastrar.ExecutarAsync(Pedido(c.Matriz.Id, "4" + c.S[..10], "So matriz", false), CancellationToken.None);
            var servico = new Servico { EmpresaId = c.Filial.Id, Descricao = "Suporte" };
            c.Db.Servicos.Add(servico);
            await c.Db.SaveChangesAsync();

            var contratos = new CadastrarContratoUseCase(c.Db, c.Sp.GetRequiredService<IAuditLogWriter>());
            CadastrarContratoRequest NaFilial(Guid clienteId) => new(
                c.Filial.Id, clienteId, "Mensal", new[] { new ContratoServicoRequest(servico.Id, 1, 100) },
                new DateOnly(2026, 11, 1), 12, null, null, null, StatusContrato.Ativo, null, TipoCobrancaContrato.Mensal, true);

            var contratoId = await contratos.ExecutarAsync(NaFilial(compartilhado), CancellationToken.None);
            Assert.NotEqual(Guid.Empty, contratoId);

            await Assert.ThrowsAsync<RecursoNaoEncontradoException>(() =>
                contratos.ExecutarAsync(NaFilial(exclusivoMatriz), CancellationToken.None));

            // Com contrato na filial, o compartilhado não pode virar exclusivo da matriz...
            var erro = await Assert.ThrowsAsync<RegraNegocioException>(() =>
                Atualizar(c).ExecutarAsync(compartilhado, Edicao("Geral", false, c.Matriz.Id), CancellationToken.None));
            Assert.Equal(RegrasClienteCompartilhado.MensagemUsadoEmOutrasEmpresas(0, 1), erro.Message);

            // ...mas pode virar exclusivo da própria filial.
            await Atualizar(c).ExecutarAsync(compartilhado, Edicao("Geral", false, c.Filial.Id), CancellationToken.None);
            var salvo = await c.Db.Clientes.AsNoTracking().SingleAsync(x => x.Id == compartilhado);
            Assert.Equal(c.Filial.Id, salvo.EmpresaId);
        }
        finally
        {
            await LimparAsync(c.Db, c.Conta.Id);
        }
    }

    [Fact]
    public async Task Documento_e_apelido_unicos_por_escopo()
    {
        using var escopo = _factory.Services.CreateScope();
        var c = await MontarAsync(escopo.ServiceProvider);
        try
        {
            var docX = "5" + c.S[..10];
            var docY = "6" + c.S[..10];
            var cadastrar = Cadastrar(c);
            await cadastrar.ExecutarAsync(Pedido(c.Matriz.Id, docX, "Acme", true), CancellationToken.None);

            // Outro compartilhado com o mesmo documento.
            var erroDoc = await Assert.ThrowsAsync<RegraNegocioException>(() =>
                cadastrar.ExecutarAsync(Pedido(c.Filial.Id, docX, "Outro", true), CancellationToken.None));
            Assert.Equal(RegrasClienteCompartilhado.MensagemDocumentoCompartilhadoRepetido(docX), erroDoc.Message);

            // Apelido do compartilhado vale na lista de todas as empresas (sem diferenciar maiúsculas).
            await Assert.ThrowsAsync<RegraNegocioException>(() =>
                cadastrar.ExecutarAsync(Pedido(c.Filial.Id, docY, "ACME", false), CancellationToken.None));

            // O exclusivo do MESMO documento esconde o compartilhado na filial: mesmo apelido pode.
            await cadastrar.ExecutarAsync(Pedido(c.Filial.Id, docX, "acme", false), CancellationToken.None);

            // Compartilhado novo com documento que a empresa da tela já tem só para ela.
            await cadastrar.ExecutarAsync(Pedido(c.Matriz.Id, docY, "Beta", false), CancellationToken.None);
            var erroExclusivo = await Assert.ThrowsAsync<RegraNegocioException>(() =>
                cadastrar.ExecutarAsync(Pedido(c.Matriz.Id, docY, "Beta geral", true), CancellationToken.None));
            Assert.Equal(RegrasClienteCompartilhado.MensagemExclusivoNestaEmpresa(docY), erroExclusivo.Message);

            // Backstop do banco: índice parcial (TenantId, CpfCnpj) WHERE EmpresaId IS NULL.
            c.Db.ChangeTracker.Clear();
            c.Db.Clientes.Add(new Cliente { EmpresaId = null, CpfCnpj = docX, Nome = "Direto", Apelido = "Direto " + c.S[..6] });
            await Assert.ThrowsAsync<DbUpdateException>(() => c.Db.SaveChangesAsync());
        }
        finally
        {
            await LimparAsync(c.Db, c.Conta.Id);
        }
    }

    [Theory]
    [InlineData(PerfilConta.Empresa, true, true)]
    [InlineData(PerfilConta.Contador, true, false)]
    [InlineData(PerfilConta.Empresa, false, false)]
    public async Task Sem_escolha_vale_o_padrao_da_conta(PerfilConta perfil, bool comFilial, bool esperadoCompartilhado)
    {
        using var escopo = _factory.Services.CreateScope();
        var c = await MontarAsync(escopo.ServiceProvider, perfil, comFilial);
        try
        {
            var disponibilidade = await new ObterDisponibilidadeClienteUseCase(c.Db, c.Sp.GetRequiredService<ICurrentTenant>())
                .ExecutarAsync(CancellationToken.None);
            Assert.Equal(comFilial, disponibilidade.MostrarCampo);
            Assert.Equal(esperadoCompartilhado, disponibilidade.PadraoCompartilhado);

            var id = await Cadastrar(c).ExecutarAsync(Pedido(c.Matriz.Id, "7" + c.S[..10], "Padrao", compartilhado: null), CancellationToken.None);
            var salvo = await c.Db.Clientes.AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.Equal(esperadoCompartilhado, salvo.EmpresaId is null);
        }
        finally
        {
            await LimparAsync(c.Db, c.Conta.Id);
        }
    }

    [Fact]
    public async Task Compartilhado_nao_aparece_para_outra_conta()
    {
        Guid contaA;
        Guid compartilhado;
        using (var escopoA = _factory.Services.CreateScope())
        {
            var a = await MontarAsync(escopoA.ServiceProvider);
            contaA = a.Conta.Id;
            compartilhado = await Cadastrar(a).ExecutarAsync(Pedido(a.Matriz.Id, "8" + a.S[..10], "Isolado", true), CancellationToken.None);
        }

        using var escopoB = _factory.Services.CreateScope();
        var b = await MontarAsync(escopoB.ServiceProvider);
        try
        {
            Assert.Empty(await ListarAsync(b, b.Matriz.Id));
            Assert.False(await b.Db.Clientes.AnyAsync(x => x.Id == compartilhado));
            await Assert.ThrowsAsync<RecursoNaoEncontradoException>(() =>
                new ObterClientePorIdUseCase(b.Db).ExecutarAsync(compartilhado, CancellationToken.None));
        }
        finally
        {
            await LimparAsync(b.Db, b.Conta.Id);
            await LimparAsync(b.Db, contaA);
        }
    }
}
