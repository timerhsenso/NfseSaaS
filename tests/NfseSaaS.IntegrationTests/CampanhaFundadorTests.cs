using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Cobranca;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// Campanha de lançamento com o banco real: vaga guardada para quem entra,
/// fundador pela reserva mesmo sem vaga sobrando, primeira cobrança no fim
/// do teste, cancelamento que mantém os dias do teste, data-fim da campanha
/// e reserva sem prazo no beta.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class CampanhaFundadorTests
{
    private readonly AplicacaoTesteFactory _factory;

    public CampanhaFundadorTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class GatewayFalso : IGatewayCobranca
    {
        public List<(decimal Valor, DateOnly Vencimento)> Assinaturas { get; } = new();
        public bool Configurado => true;
        public Task<string> CriarClienteAsync(string nome, string cpfCnpj, string? email, string referenciaExterna, CancellationToken cancellationToken) =>
            Task.FromResult("cus_" + Guid.NewGuid().ToString("N"));
        public Task<string> CriarAssinaturaMensalAsync(string clienteId, decimal valor, DateOnly primeiroVencimento, string descricao, string referenciaExterna, CancellationToken cancellationToken)
        {
            Assinaturas.Add((valor, primeiroVencimento));
            return Task.FromResult("sub_" + Guid.NewGuid().ToString("N"));
        }
        public Task<string?> ObterUrlFaturaPendenteAsync(string assinaturaId, CancellationToken cancellationToken) => Task.FromResult<string?>("https://asaas.teste/i/1");
        public Task AlterarValorAssinaturaAsync(string assinaturaId, decimal valor, string descricao, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CancelarAssinaturaAsync(string assinaturaId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<FaturaGateway?> ObterProximaFaturaEmAbertoAsync(string assinaturaId, string? ignorarPagamentoId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RemoverFaturaAsync(string pagamentoId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DateOnly?> ObterProximoVencimentoAsync(string assinaturaId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AssinaturaGatewayConciliacao?> ObterParaConciliacaoAsync(string assinaturaId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AlterarProximoVencimentoAsync(string assinaturaId, DateOnly novoVencimento, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static CobrancaService Servico(IServiceProvider sp, IGatewayCobranca gateway, bool cobrancaAtiva = true) => new(
        sp.GetRequiredService<AppDbContext>(),
        sp.GetRequiredService<ICurrentTenant>(),
        sp.GetRequiredService<ICurrentUser>(),
        gateway,
        sp.GetRequiredService<IAuditLogWriter>(),
        sp.GetRequiredService<ICatalogoPlanos>(),
        Options.Create(new CobrancaOptions { Ativa = cobrancaAtiva }),
        NullLogger<CobrancaService>.Instance);

    private static async Task<Tenant> ContaComEmailAsync(AppDbContext db, bool confirmado)
    {
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Campanha " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        db.Users.Add(new ApplicationUser { TenantId = conta.Id, UserName = $"f{s}@teste.com", Email = $"f{s}@teste.com", EmailConfirmed = confirmado });
        await db.SaveChangesAsync();
        return conta;
    }

    private static async Task LimparAsync(AppDbContext db, params Guid[] contas)
    {
        db.ChangeTracker.Clear();
        foreach (var id in contas)
        {
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync();
            await db.Users.Where(x => x.TenantId == id).ExecuteDeleteAsync();
            await db.Assinaturas.Where(x => x.TenantId == id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Quem_entra_guarda_a_vaga_assina_como_fundador_no_fim_do_teste_e_cancelar_mantem_os_dias()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var admin = sp.GetRequiredService<IAdministrarPlanosUseCase>();
        var catalogo = sp.GetRequiredService<ICatalogoPlanos>();
        var vagasAntes = (await admin.ListarVagasAsync(CancellationToken.None)).Single(v => v.Publico == PublicoPlano.Empresas);

        var primeira = await ContaComEmailAsync(db, confirmado: true);
        var segunda = await ContaComEmailAsync(db, confirmado: true);
        var naoConfirmada = await ContaComEmailAsync(db, confirmado: false);
        var gateway = new GatewayFalso();
        var servico = Servico(sp, gateway);
        // O Histórico (auditoria) da alteração de vagas e da assinatura fica na conta de teste.
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(primeira.Id);

        try
        {
            // Sobra exatamente 1 vaga no grupo das empresas.
            await admin.AlterarVagasAsync(new AlterarVagasFundadorRequest(PublicoPlano.Empresas, vagasAntes.Usadas + 1, "teste"), null, CancellationToken.None);

            Assert.False(await servico.TentarReservarAsync(naoConfirmada.Id, CancellationToken.None));
            Assert.True(await servico.TentarReservarAsync(primeira.Id, CancellationToken.None));
            Assert.False(await servico.TentarReservarAsync(primeira.Id, CancellationToken.None)); // não reserva duas vezes
            Assert.Equal(0, await catalogo.VagasFundadorRestantesAsync(PublicoPlano.Empresas, CancellationToken.None));
            Assert.False(await servico.TentarReservarAsync(segunda.Id, CancellationToken.None)); // acabou a vaga

            var listada = (await admin.ListarVagasAsync(CancellationToken.None)).Single(v => v.Publico == PublicoPlano.Empresas);
            Assert.True(listada.Reservas >= 1);
            Assert.False(listada.CampanhaAberta);

            // A tela mostra a reserva e o preço de fundador mesmo sem vaga sobrando.
            IObterAssinaturaUseCase obter = servico;
            var tela = await obter.ExecutarAsync(CancellationToken.None);
            Assert.True(tela.FundadorReservado);
            Assert.False(tela.FundadorReservaSemPrazo);
            Assert.Null(tela.FundadorReservaAte); // o teste ainda não começou
            var hoje = DataBrasilia.Hoje();
            Assert.Equal(hoje.AddDays(Planos.TesteDias), tela.PrimeiraCobrancaEm);
            var oferta = tela.Planos.Single(p => p.Codigo == Planos.CodigoMico);
            Assert.True(oferta.PrecoDeFundador);

            // Assinar antes de emitir: o teste começa agora e a fatura vence no fim dele.
            IAssinarUseCase assinar = servico;
            await assinar.ExecutarAsync(new AssinarRequest(Planos.CodigoMico), CancellationToken.None);
            var mico = (await catalogo.ObterAsync(Planos.CodigoMico, CancellationToken.None))!;
            var (valor, vencimento) = Assert.Single(gateway.Assinaturas);
            Assert.Equal(mico.PrecoFundador, valor);
            Assert.Equal(hoje.AddDays(Planos.TesteDias), vencimento);

            db.ChangeTracker.Clear();
            var assinatura = await db.Assinaturas.AsNoTracking().SingleAsync(a => a.TenantId == primeira.Id);
            Assert.True(assinatura.Fundador);
            Assert.Null(assinatura.FundadorReservadoEm); // a reserva foi usada
            Assert.NotNull(assinatura.TesteIniciadoEm);
            Assert.Equal(vencimento, assinatura.PrimeiraCobrancaEm);
            Assert.Equal(StatusAssinatura.Teste, assinatura.Status);
            await servico.GarantirPodeEmitirEmProducaoAsync(primeira.Id, CancellationToken.None);

            // Cancelar no meio do teste: a emissão continua até o fim do teste.
            ICancelarAssinaturaUseCase cancelar = servico;
            await cancelar.ExecutarAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            assinatura = await db.Assinaturas.AsNoTracking().SingleAsync(a => a.TenantId == primeira.Id);
            Assert.Equal(StatusAssinatura.Cancelada, assinatura.Status);
            Assert.Equal(vencimento, assinatura.AcessoAteAposCancelar);
            await servico.GarantirPodeEmitirEmProducaoAsync(primeira.Id, CancellationToken.None);

            // Quem cancelou não reserva de novo, e o "fundador" usado continua contando como vaga usada.
            Assert.False(await servico.TentarReservarAsync(primeira.Id, CancellationToken.None));
        }
        finally
        {
            await admin.AlterarVagasAsync(new AlterarVagasFundadorRequest(PublicoPlano.Empresas, vagasAntes.Quantidade, "teste", vagasAntes.CampanhaAte), null, CancellationToken.None);
            await LimparAsync(db, primeira.Id, segunda.Id, naoConfirmada.Id);
        }
    }

    [Fact]
    public async Task Campanha_encerrada_pela_data_nao_reserva_e_no_beta_a_reserva_e_sem_prazo()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var admin = sp.GetRequiredService<IAdministrarPlanosUseCase>();
        var catalogo = sp.GetRequiredService<ICatalogoPlanos>();
        var vagasAntes = (await admin.ListarVagasAsync(CancellationToken.None)).Single(v => v.Publico == PublicoPlano.Empresas);
        var conta = await ContaComEmailAsync(db, confirmado: true);
        var hoje = DataBrasilia.Hoje();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        try
        {
            // Data-fim ontem: sobra vaga, mas a campanha acabou.
            await admin.AlterarVagasAsync(new AlterarVagasFundadorRequest(PublicoPlano.Empresas, vagasAntes.Usadas + 5, "teste", hoje.AddDays(-1)), null, CancellationToken.None);
            Assert.Equal(0, await catalogo.VagasFundadorRestantesAsync(PublicoPlano.Empresas, CancellationToken.None));
            Assert.False(await Servico(sp, new GatewayFalso()).TentarReservarAsync(conta.Id, CancellationToken.None));
            // A varredura diária também não reserva ninguém (e não cria assinatura vazia).
            Assert.Equal(0, await Servico(sp, new GatewayFalso()).ReservarPendentesAsync(CancellationToken.None));
            Assert.False(await db.Assinaturas.AnyAsync(a => a.TenantId == conta.Id));

            // Data-fim hoje (inclusive), cobrança desligada (beta): reserva sem prazo.
            await admin.AlterarVagasAsync(new AlterarVagasFundadorRequest(PublicoPlano.Empresas, vagasAntes.Usadas + 5, "teste", hoje), null, CancellationToken.None);
            Assert.True(await Servico(sp, new GatewayFalso(), cobrancaAtiva: false).TentarReservarAsync(conta.Id, CancellationToken.None));

            db.ChangeTracker.Clear();
            var assinatura = await db.Assinaturas.AsNoTracking().SingleAsync(a => a.TenantId == conta.Id);
            Assert.NotNull(assinatura.FundadorReservadoEm);
            Assert.True(assinatura.FundadorReservaSemPrazo);
            Assert.Equal(PublicoPlano.Empresas, assinatura.FundadorReservaGrupo);
        }
        finally
        {
            await admin.AlterarVagasAsync(new AlterarVagasFundadorRequest(PublicoPlano.Empresas, vagasAntes.Quantidade, "teste", vagasAntes.CampanhaAte), null, CancellationToken.None);
            await LimparAsync(db, conta.Id);
        }
    }
}
