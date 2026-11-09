using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Application.UseCases.DadosConta;
using NfseSaaS.Application.UseCases.Plataforma;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Certificates;
using NfseSaaS.Infrastructure.DadosConta;
using NfseSaaS.Infrastructure.Documents;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>D2 com o banco real: encerrar (cliente e operador), expurgar aos 90 dias e reabrir.</summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class EncerramentoContaTests
{
    private readonly AplicacaoTesteFactory _factory;

    public EncerramentoContaTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class UsuarioFalso(Guid id) : ICurrentUser
    {
        public Guid? UserId { get; } = id;
        public string? IpAddress => "127.0.0.1";
    }

    private sealed class EmailFalso : IEmailSender
    {
        public List<string> Corpos { get; } = [];
        public Task<ResultadoEnvioEmail> EnviarAsync(string destinatarioEmail, string assunto, string corpoHtml, CancellationToken cancellationToken)
        {
            Corpos.Add(corpoHtml);
            return Task.FromResult(new ResultadoEnvioEmail(true, null));
        }
        public Task<ResultadoEnvioEmail> EnviarAsync(string destinatarioEmail, string assunto, string corpoHtml, IReadOnlyCollection<AnexoEmail> anexos, CancellationToken cancellationToken) =>
            EnviarAsync(destinatarioEmail, assunto, corpoHtml, cancellationToken);
        public string UltimoCodigo() => Regex.Match(Corpos[^1], @">(\d{6})<").Groups[1].Value;
    }

    private sealed class FilaFalsa : IBackgroundEmailQueue
    {
        public void Enfileirar(ItemFilaEmail item) { }
        public async IAsyncEnumerable<ItemFilaEmail> LerTodosAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class GatewayFalso : IGatewayCobranca
    {
        public List<string> Canceladas { get; } = [];
        public bool Configurado => true;
        public Task CancelarAssinaturaAsync(string assinaturaId, CancellationToken cancellationToken)
        {
            Canceladas.Add(assinaturaId);
            return Task.CompletedTask;
        }
        public Task<AssinaturaGatewayConciliacao?> ObterParaConciliacaoAsync(string assinaturaId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> CriarClienteAsync(string nome, string cpfCnpj, string? email, string referenciaExterna, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> CriarAssinaturaMensalAsync(string clienteId, decimal valor, DateOnly primeiroVencimento, string descricao, string referenciaExterna, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ObterUrlFaturaPendenteAsync(string assinaturaId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AlterarValorAssinaturaAsync(string assinaturaId, decimal valor, string descricao, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FaturaGateway?> ObterProximaFaturaEmAbertoAsync(string assinaturaId, string? ignorarPagamentoId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RemoverFaturaAsync(string pagamentoId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DateOnly?> ObterProximoVencimentoAsync(string assinaturaId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AlterarProximoVencimentoAsync(string assinaturaId, DateOnly novoVencimento, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static EncerramentoContaService Servico(IServiceProvider sp, Guid usuarioId, IEmailSender email, IGatewayCobranca gateway, IMemoryCache cache)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        return new EncerramentoContaService(
            db,
            sp.GetRequiredService<ICurrentTenant>(),
            new UsuarioFalso(usuarioId),
            cache,
            email,
            new EmailQueueService(db, new FilaFalsa()),
            sp.GetRequiredService<IAuditLogWriter>(),
            gateway,
            sp.GetRequiredService<ExclusaoContaService>(),
            sp.GetRequiredService<CertificateFileStore>(),
            sp.GetRequiredService<ContratoDocumentoFileStore>(),
            sp.GetRequiredService<IRecurringJobManager>(),
            Options.Create(new PlataformaOptions()),
            NullLogger<EncerramentoContaService>.Instance);
    }

    private sealed record Montagem(Tenant Conta, ApplicationUser Admin, ApplicationUser Comum, Empresa Empresa, Cliente ClienteComNota, Cliente ClienteSemNota, Contrato Contrato, Nfse? Nota);

    private static async Task<Montagem> MontarAsync(AppDbContext db, string s, bool comNota, string? asaasId = null)
    {
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Encerrar " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        db.Assinaturas.Add(new Assinatura { TenantId = conta.Id, Status = asaasId is null ? StatusAssinatura.Teste : StatusAssinatura.Ativa, AsaasClienteId = asaasId is null ? null : "cus_" + s, AsaasAssinaturaId = asaasId });
        await db.SaveChangesAsync();

        db.TenantIdOverrideDeSistema = conta.Id;
        var grupoAdmin = new Grupo { Nome = "Admin " + s, EhAdministrador = true };
        var grupoComum = new Grupo { Nome = "Comum " + s };
        db.Grupos.AddRange(grupoAdmin, grupoComum);
        await db.SaveChangesAsync();
        var admin = new ApplicationUser { TenantId = conta.Id, UserName = $"adm{s}@t.com", Email = $"adm{s}@t.com", GrupoId = grupoAdmin.Id, SecurityStamp = "ANTIGO" };
        var comum = new ApplicationUser { TenantId = conta.Id, UserName = $"com{s}@t.com", Email = $"com{s}@t.com", GrupoId = grupoComum.Id };
        db.Users.AddRange(admin, comum);
        var empresa = new Empresa { Cnpj = s[..14], RazaoSocial = "Empresa " + s };
        db.Empresas.Add(empresa);
        await db.SaveChangesAsync();
        var clienteComNota = new Cliente { EmpresaId = empresa.Id, Nome = "Com nota", Apelido = "CN" + s[..6], CpfCnpj = s[..11], Email = "tomador@x.com", Telefone = "71999990000" };
        var clienteSemNota = new Cliente { EmpresaId = empresa.Id, Nome = "Sem nota", Apelido = "SN" + s[..6], CpfCnpj = s[1..12] };
        var servico = new Servico { EmpresaId = empresa.Id, Descricao = "Suporte" };
        db.Clientes.AddRange(clienteComNota, clienteSemNota);
        db.Servicos.Add(servico);
        await db.SaveChangesAsync();
        var contrato = new Contrato { EmpresaId = empresa.Id, ClienteId = clienteComNota.Id, Descricao = "Mensal", ValorAtual = 10, DataInicioContrato = new DateOnly(2026, 1, 1) };
        db.Contratos.Add(contrato);
        await db.SaveChangesAsync();
        db.ContratoServicos.Add(new ContratoServico { ContratoId = contrato.Id, ServicoId = servico.Id, Quantidade = 1, ValorUnitario = 10 });
        db.ConvitesIndicacao.Add(new ConviteIndicacao { Email = $"convidado{s}@x.com", EnviadoPorUsuarioId = admin.Id });
        Nfse? nota = null;
        if (comNota)
        {
            nota = new Nfse
            {
                EmpresaId = empresa.Id, ClienteId = clienteComNota.Id, ContratoId = contrato.Id, ServicoId = servico.Id,
                NumeroDps = 1, SerieDps = "1", DataCompetencia = new DateOnly(2026, 10, 1), ValorServico = 10,
                Status = NfseStatus.Autorizada, TipoAmbiente = TipoAmbiente.Producao, ChaveAcesso = "CHAVE" + s, XmlNfse = "<NFSe/>"
            };
            db.NotasFiscais.Add(nota);
        }
        await db.SaveChangesAsync();
        db.TenantIdOverrideDeSistema = null;
        db.ChangeTracker.Clear();
        return new Montagem(conta, admin, comum, empresa, clienteComNota, clienteSemNota, contrato, nota);
    }

    private static async Task LimparAsync(AppDbContext db, Guid tenantId)
    {
        db.ChangeTracker.Clear();
        await db.NotasFiscais.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.ContratoServicos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Contratos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Servicos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Clientes.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.ConvitesIndicacao.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.EmailLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.GrupoTelas.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Grupos.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Assinaturas.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Tenants.Where(x => x.Id == tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Administrador_encerra_com_codigo_cancela_a_assinatura_e_fecha_o_acesso()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var m = await MontarAsync(db, s, comNota: true, asaasId: "sub_" + s);
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(m.Conta.Id);
        var email = new EmailFalso();
        var gateway = new GatewayFalso();
        var cache = new MemoryCache(new MemoryCacheOptions());

        try
        {
            // Usuário comum não encerra.
            var comum = Servico(sp, m.Comum.Id, email, gateway, cache);
            Assert.False((await comum.ObterSituacaoAsync(CancellationToken.None)).PodeEncerrar);
            await Assert.ThrowsAsync<RegraNegocioException>(() => comum.SolicitarCodigoAsync(CancellationToken.None));

            // Administrador: vê que há assinatura e nota, pede o código.
            var admin = Servico(sp, m.Admin.Id, email, gateway, cache);
            var situacao = await admin.ObterSituacaoAsync(CancellationToken.None);
            Assert.True(situacao.PodeEncerrar);
            Assert.True(situacao.TemAssinaturaAtiva);
            Assert.Equal(1, situacao.Notas);
            var pedido = await admin.SolicitarCodigoAsync(CancellationToken.None);
            Assert.EndsWith("@t.com", pedido.EmailMascarado);
            Assert.False(await db.EmailLogs.IgnoreQueryFilters().AnyAsync(e => e.TenantId == m.Conta.Id), "o código não pode ir para o EmailLog");

            // Código errado não encerra.
            await Assert.ThrowsAsync<RegraNegocioException>(() => admin.ConfirmarAsync(new ConfirmarEncerramentoRequest("000000" == email.UltimoCodigo() ? "111111" : "000000"), CancellationToken.None));
            Assert.Null((await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == m.Conta.Id)).EncerradaEm);

            var r = await admin.ConfirmarAsync(new ConfirmarEncerramentoRequest(email.UltimoCodigo()), CancellationToken.None);
            Assert.Equal(DataBrasilia.Hoje().AddDays(90), r.ExpurgoPrevistoEm);

            var conta = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == m.Conta.Id);
            Assert.False(conta.Ativo);
            Assert.NotNull(conta.EncerradaEm);
            Assert.Equal(OrigemEncerramento.Cliente, conta.EncerramentoOrigem);
            Assert.Equal(r.ExpurgoPrevistoEm, conta.ExpurgoPrevistoEm);
            Assert.Equal("Conta encerrada a pedido do cliente.", conta.MotivoBloqueio);

            Assert.Equal(["sub_" + s], gateway.Canceladas);
            Assert.Equal(StatusAssinatura.Cancelada, (await db.Assinaturas.AsNoTracking().SingleAsync(a => a.TenantId == m.Conta.Id)).Status);
            Assert.NotEqual("ANTIGO", (await db.Users.AsNoTracking().SingleAsync(u => u.Id == m.Admin.Id)).SecurityStamp);
            Assert.True(await db.EmailLogs.IgnoreQueryFilters().AnyAsync(e => e.TenantId == m.Conta.Id && e.Tipo == TipoEmailCatalogo.EncerramentoConta));
            Assert.True(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(l => l.TenantId == m.Conta.Id && l.Operacao == "ContaEncerrada"));

            // Já encerrada: não pede de novo.
            await Assert.ThrowsAsync<RegraNegocioException>(() => admin.SolicitarCodigoAsync(CancellationToken.None));
        }
        finally
        {
            await LimparAsync(db, m.Conta.Id);
        }
    }

    [Fact]
    public async Task Expurgo_guarda_as_notas_apaga_o_resto_e_conta_sem_nota_some_inteira()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s1 = Guid.NewGuid().ToString("N");
        var s2 = Guid.NewGuid().ToString("N");
        var s3 = Guid.NewGuid().ToString("N");
        var comNota = await MontarAsync(db, s1, comNota: true);
        var semNota = await MontarAsync(db, s2, comNota: false);
        var futura = await MontarAsync(db, s3, comNota: false);
        var hoje = DataBrasilia.Hoje();
        var servico = Servico(sp, Guid.NewGuid(), new EmailFalso(), new GatewayFalso(), new MemoryCache(new MemoryCacheOptions()));

        try
        {
            // Encerradas pelo operador; duas com o prazo vencido, uma ainda no prazo.
            foreach (var encerrar in new[] { comNota.Conta, semNota.Conta, futura.Conta })
                await servico.EncerrarPelaPlataformaAsync(encerrar.Id, "teste", Guid.NewGuid(), CancellationToken.None);
            await db.Tenants.Where(t => t.Id == comNota.Conta.Id || t.Id == semNota.Conta.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.ExpurgoPrevistoEm, hoje.AddDays(-1)));

            // Reabrir antes do expurgo funciona (e depois volta a encerrar).
            var alterarBloqueio = sp.GetRequiredService<IAlterarBloqueioTenantUseCase>();
            await alterarBloqueio.ExecutarAsync(futura.Conta.Id, false, null, Guid.NewGuid(), CancellationToken.None);
            var reaberta = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == futura.Conta.Id);
            Assert.True(reaberta.Ativo);
            Assert.Null(reaberta.EncerradaEm);
            Assert.Null(reaberta.ExpurgoPrevistoEm);
            await servico.EncerrarPelaPlataformaAsync(futura.Conta.Id, "de novo", Guid.NewGuid(), CancellationToken.None);

            var resultado = await servico.ExecutarAsync(hoje, CancellationToken.None);
            Assert.True(resultado.Anonimizadas >= 1);
            Assert.True(resultado.Excluidas >= 1);
            Assert.Equal(0, resultado.ComErro);
            db.ChangeTracker.Clear();

            // Sem nota: a conta some inteira.
            Assert.False(await db.Tenants.AnyAsync(t => t.Id == semNota.Conta.Id));

            // Com nota: fica a nota, a empresa, o tomador da nota (sem contato), a conta e a cobrança.
            var conta = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == comNota.Conta.Id);
            Assert.NotNull(conta.DadosExpurgadosEm);
            var nota = await db.NotasFiscais.IgnoreQueryFilters().AsNoTracking().SingleAsync(n => n.TenantId == comNota.Conta.Id);
            Assert.Null(nota.ContratoId);
            Assert.Null(nota.ServicoId);
            Assert.Equal("<NFSe/>", nota.XmlNfse);
            var tomador = await db.Clientes.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.TenantId == comNota.Conta.Id);
            Assert.Equal(comNota.ClienteComNota.Id, tomador.Id);
            Assert.Null(tomador.Email);
            Assert.Null(tomador.Telefone);
            Assert.False((await db.Empresas.IgnoreQueryFilters().AsNoTracking().SingleAsync(e => e.TenantId == comNota.Conta.Id)).Ativo);
            Assert.True(await db.Assinaturas.AnyAsync(a => a.TenantId == comNota.Conta.Id));

            Assert.False(await db.Users.AnyAsync(u => u.TenantId == comNota.Conta.Id));
            Assert.False(await db.Grupos.IgnoreQueryFilters().AnyAsync(g => g.TenantId == comNota.Conta.Id));
            Assert.False(await db.Contratos.IgnoreQueryFilters().AnyAsync(c => c.TenantId == comNota.Conta.Id));
            Assert.False(await db.Servicos.IgnoreQueryFilters().AnyAsync(c => c.TenantId == comNota.Conta.Id));
            Assert.False(await db.ConvitesIndicacao.IgnoreQueryFilters().AnyAsync(c => c.TenantId == comNota.Conta.Id));
            Assert.False(await db.EmailLogs.IgnoreQueryFilters().AnyAsync(c => c.TenantId == comNota.Conta.Id));
            var historico = await db.AuditLogs.IgnoreQueryFilters().Where(l => l.TenantId == comNota.Conta.Id).Select(l => l.Operacao).ToListAsync();
            Assert.Equal(["DadosDaContaApagados"], historico);

            // Dentro do prazo: nada apagado.
            Assert.True(await db.Users.AnyAsync(u => u.TenantId == futura.Conta.Id));

            // Depois do expurgo não dá para reabrir; e a passada seguinte não faz nada com ela.
            var naoReabre = await Assert.ThrowsAsync<RegraNegocioException>(() =>
                alterarBloqueio.ExecutarAsync(comNota.Conta.Id, false, null, Guid.NewGuid(), CancellationToken.None));
            Assert.Equal(RegrasEncerramentoConta.MotivoJaExpurgada, naoReabre.Message);
            var deNovo = await servico.ExecutarAsync(hoje, CancellationToken.None);
            Assert.Equal(0, deNovo.ComErro);
            Assert.Equal(conta.DadosExpurgadosEm, (await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == comNota.Conta.Id)).DadosExpurgadosEm);
        }
        finally
        {
            await LimparAsync(db, comNota.Conta.Id);
            await LimparAsync(db, semNota.Conta.Id);
            await LimparAsync(db, futura.Conta.Id);
        }
    }
}
