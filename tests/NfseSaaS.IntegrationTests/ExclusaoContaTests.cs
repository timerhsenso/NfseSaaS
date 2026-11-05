using System.Text.RegularExpressions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Plataforma;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Certificates;
using NfseSaaS.Infrastructure.Documents;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// Exclusão de conta pelo operador, com o banco real: código por e-mail
/// (falso aqui), código errado e de uso único, tudo da conta apagado, e os
/// bloqueios (nota registrada, assinatura ativa, a própria conta).
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class ExclusaoContaTests
{
    private readonly AplicacaoTesteFactory _factory;

    public ExclusaoContaTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class EmailFalso : IEmailSender
    {
        public List<(string Para, string Corpo)> Enviados { get; } = new();

        public Task<ResultadoEnvioEmail> EnviarAsync(string destinatarioEmail, string assunto, string corpoHtml, CancellationToken cancellationToken)
        {
            Enviados.Add((destinatarioEmail, corpoHtml));
            return Task.FromResult(new ResultadoEnvioEmail(true, null));
        }

        public Task<ResultadoEnvioEmail> EnviarAsync(string destinatarioEmail, string assunto, string corpoHtml, IReadOnlyCollection<AnexoEmail> anexos, CancellationToken cancellationToken) =>
            EnviarAsync(destinatarioEmail, assunto, corpoHtml, cancellationToken);

        public string UltimoCodigo() => Regex.Match(Enviados[^1].Corpo, @">(\d{6})<").Groups[1].Value;
    }

    private static ExclusaoContaService Servico(IServiceProvider sp, IEmailSender email, IMemoryCache cache) => new(
        sp.GetRequiredService<AppDbContext>(),
        sp.GetRequiredService<ICurrentTenant>(),
        cache,
        email,
        sp.GetRequiredService<IAuditLogWriter>(),
        sp.GetRequiredService<CertificateFileStore>(),
        sp.GetRequiredService<ContratoDocumentoFileStore>(),
        sp.GetRequiredService<IRecurringJobManager>(),
        Options.Create(new PlataformaOptions()),
        NullLogger<ExclusaoContaService>.Instance);

    /// <summary>Conta do operador (com um usuário que recebe o código) e a conta a excluir com um pouco de tudo.</summary>
    private static async Task<(Tenant Operador, ApplicationUser UsuarioOperador, Tenant Conta, Empresa Empresa, Cliente Cliente, Tenant Indicada)> MontarAsync(AppDbContext db, string s)
    {
        var operador = new Tenant { Documento = s[..14], RazaoSocial = "Operador " + s };
        var conta = new Tenant { Documento = s[14..28], RazaoSocial = "Conta " + s, IndicadoPorTenantId = null };
        db.Tenants.AddRange(operador, conta);
        await db.SaveChangesAsync();

        conta.IndicadoPorTenantId = operador.Id;
        var indicada = new Tenant { Documento = s[4..18], RazaoSocial = "Indicada " + s, IndicadoPorTenantId = conta.Id };
        db.Tenants.Add(indicada);

        var usuarioOperador = new ApplicationUser { TenantId = operador.Id, UserName = $"op{s}@teste.com", Email = $"op{s}@teste.com" };
        db.Users.Add(usuarioOperador);
        await db.SaveChangesAsync();

        db.TenantIdOverrideDeSistema = conta.Id;
        var grupo = new Grupo { Nome = "Admin " + s };
        db.Grupos.Add(grupo);
        await db.SaveChangesAsync();
        var empresa = new Empresa { Cnpj = s[..14], RazaoSocial = "Empresa " + s };
        db.Empresas.Add(empresa);
        await db.SaveChangesAsync();
        var cliente = new Cliente { EmpresaId = empresa.Id, Nome = "Cliente", Apelido = "C" + s[..6], CpfCnpj = s[..11] };
        var servico = new Servico { EmpresaId = empresa.Id, Descricao = "Serviço" };
        db.Clientes.Add(cliente);
        db.Servicos.Add(servico);
        await db.SaveChangesAsync();
        var contrato = new Contrato { EmpresaId = empresa.Id, ClienteId = cliente.Id, Descricao = "Contrato", ValorAtual = 10, DataInicioContrato = new DateOnly(2026, 1, 1) };
        db.Contratos.Add(contrato);
        await db.SaveChangesAsync();
        db.ContratoServicos.Add(new ContratoServico { ContratoId = contrato.Id, ServicoId = servico.Id, Quantidade = 1, ValorUnitario = 10 });
        db.Users.Add(new ApplicationUser { TenantId = conta.Id, UserName = $"u{s}@teste.com", Email = $"u{s}@teste.com", GrupoId = grupo.Id });
        db.AuditLogs.Add(new AuditLog { Operacao = "Teste", Entidade = "Teste" });
        db.EmailLogs.Add(new EmailLog { Tipo = "Teste", Destinatario = "x@x.com", Assunto = "x", CorpoHtml = "x" });
        await db.SaveChangesAsync();
        db.TenantIdOverrideDeSistema = null;

        db.Assinaturas.Add(new Assinatura { TenantId = conta.Id, AsaasAssinaturaId = "sub_x_" + s, Status = StatusAssinatura.Cancelada });
        db.PremiosIndicacao.Add(new PremioIndicacao { TenantId = operador.Id, IndicadoTenantId = conta.Id, Papel = PapelPremioIndicacao.QuemIndicou });
        await db.SaveChangesAsync();

        return (operador, usuarioOperador, conta, empresa, cliente, indicada);
    }

    private static async Task LimparAsync(AppDbContext db, params Guid[] tenants)
    {
        db.ChangeTracker.Clear();
        await db.NotasFiscais.IgnoreQueryFilters().Where(x => tenants.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.ContratoServicos.IgnoreQueryFilters().Where(x => tenants.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Contratos.IgnoreQueryFilters().Where(x => tenants.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Servicos.IgnoreQueryFilters().Where(x => tenants.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Clientes.IgnoreQueryFilters().Where(x => tenants.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Empresas.IgnoreQueryFilters().Where(x => tenants.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.AuditLogs.IgnoreQueryFilters().Where(x => tenants.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.EmailLogs.IgnoreQueryFilters().Where(x => tenants.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Users.Where(x => tenants.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Grupos.IgnoreQueryFilters().Where(x => tenants.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.PremiosIndicacao.Where(x => tenants.Contains(x.TenantId) || tenants.Contains(x.IndicadoTenantId)).ExecuteDeleteAsync();
        await db.Assinaturas.Where(x => tenants.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Tenants.Where(x => tenants.Contains(x.Id)).ExecuteUpdateAsync(u => u.SetProperty(t => t.IndicadoPorTenantId, (Guid?)null));
        await db.Tenants.Where(x => tenants.Contains(x.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Codigo_por_email_errado_conta_tentativa_e_certo_apaga_tudo_uma_vez_so()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var (operador, usuarioOperador, conta, _, _, indicada) = await MontarAsync(db, s);

        try
        {
            sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(operador.Id);
            var email = new EmailFalso();
            var servico = Servico(sp, email, new MemoryCache(new MemoryCacheOptions()));

            var resumo = await ((ISolicitarExclusaoContaUseCase)servico).ExecutarAsync(conta.Id, usuarioOperador.Id, CancellationToken.None);
            Assert.Equal(1, resumo.Empresas);
            Assert.Equal(1, resumo.Clientes);
            Assert.Equal(1, resumo.Contratos);
            Assert.Equal(1, resumo.Servicos);
            Assert.Equal(1, resumo.Usuarios);
            Assert.Equal(CodigoExclusaoConta.MinutosValidade, resumo.MinutosValidade);
            var (para, _) = Assert.Single(email.Enviados);
            Assert.Equal(usuarioOperador.Email, para);
            var codigo = email.UltimoCodigo();
            Assert.Matches("^[0-9]{6}$", codigo);
            // O código não fica gravado no banco.
            Assert.False(await db.EmailLogs.IgnoreQueryFilters().AnyAsync(e => e.CorpoHtml.Contains(codigo)));

            var confirmar = (IConfirmarExclusaoContaUseCase)servico;
            var errado = codigo == "000000" ? "000001" : "000000";
            var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => confirmar.ExecutarAsync(conta.Id, usuarioOperador.Id, errado, CancellationToken.None));
            Assert.Contains("Restam 4", erro.Message);
            Assert.True(await db.Tenants.AnyAsync(t => t.Id == conta.Id));

            await confirmar.ExecutarAsync(conta.Id, usuarioOperador.Id, codigo, CancellationToken.None);

            db.ChangeTracker.Clear();
            Assert.False(await db.Tenants.AnyAsync(t => t.Id == conta.Id));
            Assert.False(await db.Empresas.IgnoreQueryFilters().AnyAsync(x => x.TenantId == conta.Id));
            Assert.False(await db.Clientes.IgnoreQueryFilters().AnyAsync(x => x.TenantId == conta.Id));
            Assert.False(await db.Servicos.IgnoreQueryFilters().AnyAsync(x => x.TenantId == conta.Id));
            Assert.False(await db.Contratos.IgnoreQueryFilters().AnyAsync(x => x.TenantId == conta.Id));
            Assert.False(await db.ContratoServicos.IgnoreQueryFilters().AnyAsync(x => x.TenantId == conta.Id));
            Assert.False(await db.Users.AnyAsync(x => x.TenantId == conta.Id));
            Assert.False(await db.Grupos.IgnoreQueryFilters().AnyAsync(x => x.TenantId == conta.Id));
            Assert.False(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(x => x.TenantId == conta.Id));
            Assert.False(await db.EmailLogs.IgnoreQueryFilters().AnyAsync(x => x.TenantId == conta.Id));
            Assert.False(await db.Assinaturas.AnyAsync(x => x.TenantId == conta.Id));
            Assert.False(await db.PremiosIndicacao.AnyAsync(x => x.IndicadoTenantId == conta.Id || x.TenantId == conta.Id));

            // A conta que veio pelo link dela continua, sem "indicada por".
            var indicadaDepois = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == indicada.Id);
            Assert.Null(indicadaDepois.IndicadoPorTenantId);

            // Fica só o registro no Histórico do operador, sem nome nem CNPJ.
            var registro = await db.AuditLogs.IgnoreQueryFilters().SingleAsync(a => a.TenantId == operador.Id && a.Operacao == "ContaExcluidaPelaPlataforma");
            Assert.Equal(conta.Id, registro.EntidadeId);
            Assert.DoesNotContain(conta.Documento, registro.Dados);
            Assert.DoesNotContain(conta.RazaoSocial, registro.Dados);

            // Código de uso único.
            await Assert.ThrowsAsync<RegraNegocioException>(() => confirmar.ExecutarAsync(conta.Id, usuarioOperador.Id, codigo, CancellationToken.None));
        }
        finally
        {
            await LimparAsync(db, operador.Id, conta.Id, indicada.Id);
        }
    }

    [Fact]
    public async Task Cinco_codigos_errados_cancelam_o_codigo()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var (operador, usuarioOperador, conta, _, _, indicada) = await MontarAsync(db, s);

        try
        {
            sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(operador.Id);
            var email = new EmailFalso();
            var servico = Servico(sp, email, new MemoryCache(new MemoryCacheOptions()));
            await ((ISolicitarExclusaoContaUseCase)servico).ExecutarAsync(conta.Id, usuarioOperador.Id, CancellationToken.None);
            var codigo = email.UltimoCodigo();
            var errado = codigo == "000000" ? "000001" : "000000";
            var confirmar = (IConfirmarExclusaoContaUseCase)servico;

            for (var i = 1; i < CodigoExclusaoConta.MaxTentativas; i++)
                await Assert.ThrowsAsync<RegraNegocioException>(() => confirmar.ExecutarAsync(conta.Id, usuarioOperador.Id, errado, CancellationToken.None));
            var ultimo = await Assert.ThrowsAsync<RegraNegocioException>(() => confirmar.ExecutarAsync(conta.Id, usuarioOperador.Id, errado, CancellationToken.None));
            Assert.Contains("cancelado", ultimo.Message);

            // Nem o código certo vale mais.
            await Assert.ThrowsAsync<RegraNegocioException>(() => confirmar.ExecutarAsync(conta.Id, usuarioOperador.Id, codigo, CancellationToken.None));
            Assert.True(await db.Tenants.AnyAsync(t => t.Id == conta.Id));
        }
        finally
        {
            await LimparAsync(db, operador.Id, conta.Id, indicada.Id);
        }
    }

    [Fact]
    public async Task Nao_exclui_conta_com_nota_com_assinatura_ativa_nem_a_propria()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var (operador, usuarioOperador, conta, empresa, cliente, indicada) = await MontarAsync(db, s);

        try
        {
            sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(operador.Id);
            var email = new EmailFalso();
            var pedir = (ISolicitarExclusaoContaUseCase)Servico(sp, email, new MemoryCache(new MemoryCacheOptions()));

            // A própria conta do operador.
            var propria = await Assert.ThrowsAsync<RegraNegocioException>(() => pedir.ExecutarAsync(operador.Id, usuarioOperador.Id, CancellationToken.None));
            Assert.Contains("própria conta", propria.Message);

            // Assinatura ativa no Asaas.
            await db.Assinaturas.Where(a => a.TenantId == conta.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.Status, StatusAssinatura.Ativa));
            var ativa = await Assert.ThrowsAsync<RegraNegocioException>(() => pedir.ExecutarAsync(conta.Id, usuarioOperador.Id, CancellationToken.None));
            Assert.Contains("assinatura ativa", ativa.Message);
            await db.Assinaturas.Where(a => a.TenantId == conta.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.Status, StatusAssinatura.Cancelada));

            // Qualquer nota registrada (aqui, uma rejeitada de homologação).
            db.TenantIdOverrideDeSistema = conta.Id;
            db.NotasFiscais.Add(new Nfse
            {
                EmpresaId = empresa.Id, ClienteId = cliente.Id, NumeroDps = 1, SerieDps = "1",
                DataCompetencia = new DateOnly(2026, 11, 1), Status = NfseStatus.Rejeitada, TipoAmbiente = TipoAmbiente.Homologacao
            });
            await db.SaveChangesAsync();
            db.TenantIdOverrideDeSistema = null;

            var comNota = await Assert.ThrowsAsync<RegraNegocioException>(() => pedir.ExecutarAsync(conta.Id, usuarioOperador.Id, CancellationToken.None));
            Assert.Contains("nota", comNota.Message);
            Assert.Empty(email.Enviados);
        }
        finally
        {
            await LimparAsync(db, operador.Id, conta.Id, indicada.Id);
        }
    }
}
