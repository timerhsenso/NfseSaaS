using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Cobranca;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// B2 com o banco real e um gateway falso (nunca o Asaas): prévia, agendamento
/// com aviso por e-mail, aplicação na data, cancelamento quando a assinatura
/// muda e cancelamento do lote pelo operador.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class ReajusteAssinaturaTests
{
    private readonly AplicacaoTesteFactory _factory;

    public ReajusteAssinaturaTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class GatewayFalso : IGatewayCobranca
    {
        public List<(string AssinaturaId, decimal Valor)> Alteracoes { get; } = new();
        public bool Falhar { get; set; }
        public bool Configurado => true;
        public Task AlterarValorAssinaturaAsync(string assinaturaId, decimal valor, string descricao, CancellationToken cancellationToken)
        {
            if (Falhar)
                throw new RegraNegocioException("Asaas fora do ar (teste).");
            Alteracoes.Add((assinaturaId, valor));
            return Task.CompletedTask;
        }
        public Task<string> CriarClienteAsync(string nome, string cpfCnpj, string? email, string referenciaExterna, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> CriarAssinaturaMensalAsync(string clienteId, decimal valor, DateOnly primeiroVencimento, string descricao, string referenciaExterna, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ObterUrlFaturaPendenteAsync(string assinaturaId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CancelarAssinaturaAsync(string assinaturaId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FaturaGateway?> ObterProximaFaturaEmAbertoAsync(string assinaturaId, string? ignorarPagamentoId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RemoverFaturaAsync(string pagamentoId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DateOnly?> ObterProximoVencimentoAsync(string assinaturaId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AssinaturaGatewayConciliacao?> ObterParaConciliacaoAsync(string assinaturaId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AlterarProximoVencimentoAsync(string assinaturaId, DateOnly novoVencimento, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static ReajusteAssinaturaService Servico(IServiceProvider sp, GatewayFalso gateway) => new(
        sp.GetRequiredService<AppDbContext>(),
        gateway,
        sp.GetRequiredService<IEmailQueueService>(),
        sp.GetRequiredService<IAuditLogWriter>(),
        // Cobrança ligada só aqui (a factory roda com ela desligada).
        Options.Create(new CobrancaOptions { Ativa = true }),
        Options.Create(new EmailOptions { AppBaseUrl = "https://app.teste" }),
        Options.Create(new SuporteOptions { Email = "suporte@teste.com" }),
        NullLogger<ReajusteAssinaturaService>.Instance);

    private sealed record Cenario(Plano Plano, Tenant Comum, Tenant Fundador, Tenant Recente, Assinatura AssComum, Assinatura AssFundador, Assinatura AssRecente, string Email);

    private static async Task<Cenario> MontarAsync(AppDbContext db, IServiceProvider sp, string s)
    {
        var hoje = DataBrasilia.Hoje();
        // Plano só deste teste: ninguém de outro teste aparece na prévia.
        var plano = new Plano { Codigo = 800000 + Random.Shared.Next(99999), Nome = "Reaj " + s[..6], Descricao = "teste", MaxEmpresas = 1 };
        db.Planos.Add(plano);
        db.PrecosPlanos.AddRange(
            new PrecoPlano { PlanoId = plano.Id, PrecoMensal = 29m, PrecoFundador = 19m, VigenteDesde = hoje.AddYears(-2) },
            new PrecoPlano { PlanoId = plano.Id, PrecoMensal = 35m, PrecoFundador = 19m, VigenteDesde = hoje.AddDays(31) });

        Tenant Conta(string nome) => new() { Documento = Guid.NewGuid().ToString("N")[..14], RazaoSocial = nome + " " + s };
        var comum = Conta("Comum");
        var fundador = Conta("Fundador");
        var recente = Conta("Recente");
        db.Tenants.AddRange(comum, fundador, recente);
        await db.SaveChangesAsync();

        Assinatura Ass(Tenant t, bool ehFundador, DateTimeOffset assinadaEm) => new()
        {
            TenantId = t.Id, Status = StatusAssinatura.Ativa, Plano = plano.Codigo, PrecoMensal = ehFundador ? 19m : 29m,
            Fundador = ehFundador, AsaasAssinaturaId = "sub_rj_" + Guid.NewGuid().ToString("N")[..10], AssinadaEm = assinadaEm,
            UrlFaturaPendente = "https://asaas.teste/i/" + s, VencimentoFaturaPendente = hoje.AddDays(45), ValorFaturaPendente = ehFundador ? 19m : 29m
        };
        var assComum = Ass(comum, false, DateTimeOffset.UtcNow.AddMonths(-14));
        var assFundador = Ass(fundador, true, DateTimeOffset.UtcNow.AddMonths(-14));
        var assRecente = Ass(recente, false, DateTimeOffset.UtcNow.AddMonths(-3));
        db.Assinaturas.AddRange(assComum, assFundador, assRecente);
        await db.SaveChangesAsync();

        // Administrador com e-mail confirmado só na conta comum (grupo e usuário gravados como dela).
        var email = $"adm{s[..8]}@teste.com";
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(comum.Id);
        var grupo = new Grupo { Nome = "Admin " + s, EhAdministrador = true };
        db.Grupos.Add(grupo);
        await db.SaveChangesAsync();
        db.Users.Add(new ApplicationUser { TenantId = comum.Id, UserName = email, Email = email, EmailConfirmed = true, GrupoId = grupo.Id });
        await db.SaveChangesAsync();

        // Quem agenda é o operador: outra conta qualquer (a do fundador, aqui).
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(fundador.Id);
        return new Cenario(plano, comum, fundador, recente, assComum, assFundador, assRecente, email);
    }

    private static async Task LimparAsync(AppDbContext db, Cenario c)
    {
        db.ChangeTracker.Clear();
        var contas = new[] { c.Comum.Id, c.Fundador.Id, c.Recente.Id };
        await db.EmailLogs.IgnoreQueryFilters().Where(x => contas.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.AuditLogs.IgnoreQueryFilters().Where(x => contas.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.ReajustesAssinatura.Where(x => contas.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Assinaturas.Where(x => contas.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Users.Where(x => contas.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Grupos.IgnoreQueryFilters().Where(x => contas.Contains(x.TenantId)).ExecuteDeleteAsync();
        await db.Tenants.Where(x => contas.Contains(x.Id)).ExecuteDeleteAsync();
        await db.PrecosPlanos.Where(x => x.PlanoId == c.Plano.Id).ExecuteDeleteAsync();
        await db.Planos.Where(x => x.Id == c.Plano.Id).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Previa_agenda_avisa_e_aplica_na_data_so_para_quem_entra()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var c = await MontarAsync(db, sp, Guid.NewGuid().ToString("N"));
        var gateway = new GatewayFalso();
        var servico = Servico(sp, gateway);
        var data = DataBrasilia.Hoje().AddDays(35);
        try
        {
            // Data com menos de 30 dias de aviso é recusada.
            await Assert.ThrowsAsync<RegraNegocioException>(() =>
                servico.PreviaAsync(c.Plano.Id, DataBrasilia.Hoje().AddDays(10), CancellationToken.None));

            // Preço de tabela NA DATA (o agendado de R$ 35); só a conta comum entra.
            var previa = await servico.PreviaAsync(c.Plano.Id, data, CancellationToken.None);
            Assert.Equal(35m, previa.PrecoNovo);
            Assert.Equal(1, previa.Entram);
            Assert.Contains("fundador", previa.Contas.Single(x => x.TenantId == c.Fundador.Id).Motivo);
            Assert.Contains("12 meses", previa.Contas.Single(x => x.TenantId == c.Recente.Id).Motivo);

            var agendado = await servico.AgendarAsync(c.Plano.Id, new AgendarReajusteRequest(data, "reajuste anual"), null, CancellationToken.None);
            Assert.Equal(1, agendado.Agendados);
            Assert.Equal(2, agendado.ForaDoReajuste);
            Assert.Equal(0, agendado.SemDestinatario);

            var reajuste = await db.ReajustesAssinatura.AsNoTracking().SingleAsync(r => r.LoteId == agendado.LoteId);
            Assert.Equal(c.AssComum.Id, reajuste.AssinaturaId);
            Assert.Equal(29m, reajuste.PrecoAnterior);
            Assert.NotNull(reajuste.AvisadoEm);

            // Aviso na fila, gravado como da conta avisada, com a chave do reajuste.
            var aviso = await db.EmailLogs.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(e => e.ChaveDeduplicacao == RegrasReajusteAssinatura.ChaveAviso(reajuste.Id));
            Assert.Equal(c.Comum.Id, aviso.TenantId);
            Assert.Equal(TipoEmailCatalogo.ReajusteAssinatura, aviso.Tipo);
            Assert.Equal(c.Email, aviso.Destinatario);

            // De novo na prévia: a conta comum já tem agendado.
            Assert.Equal(0, (await servico.PreviaAsync(c.Plano.Id, data, CancellationToken.None)).Entram);

            // Antes da data o job não mexe.
            Assert.Equal(0, (await servico.ExecutarAsync(CancellationToken.None)).Aplicados);

            // Chegou a data: Asaas falha uma vez (continua agendado), depois aplica.
            await db.ReajustesAssinatura.Where(r => r.Id == reajuste.Id).ExecuteUpdateAsync(x => x.SetProperty(r => r.AplicarEm, DataBrasilia.Hoje()));
            db.ChangeTracker.Clear();
            gateway.Falhar = true;
            Assert.Equal(1, (await servico.ExecutarAsync(CancellationToken.None)).Falhas);
            var depoisDaFalha = await db.ReajustesAssinatura.AsNoTracking().SingleAsync(r => r.Id == reajuste.Id);
            Assert.Equal(StatusReajusteAssinatura.Agendado, depoisDaFalha.Status);
            Assert.Equal(1, depoisDaFalha.Tentativas);
            Assert.Equal(29m, (await db.Assinaturas.AsNoTracking().SingleAsync(a => a.Id == c.AssComum.Id)).PrecoMensal);

            db.ChangeTracker.Clear();
            gateway.Falhar = false;
            Assert.Equal(1, (await servico.ExecutarAsync(CancellationToken.None)).Aplicados);
            Assert.Equal((c.AssComum.AsaasAssinaturaId!, 35m), Assert.Single(gateway.Alteracoes));

            var assinatura = await db.Assinaturas.AsNoTracking().SingleAsync(a => a.Id == c.AssComum.Id);
            Assert.Equal(35m, assinatura.PrecoMensal);
            Assert.Equal(35m, assinatura.ValorFaturaPendente);
            var aplicado = await db.ReajustesAssinatura.AsNoTracking().SingleAsync(r => r.Id == reajuste.Id);
            Assert.Equal(StatusReajusteAssinatura.Aplicado, aplicado.Status);
            Assert.True(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.TenantId == c.Comum.Id && a.Operacao == "ReajusteAssinaturaAplicado"));

            // Já paga o preço novo: não entra de novo.
            var outra = await servico.PreviaAsync(c.Plano.Id, data, CancellationToken.None);
            Assert.False(outra.Contas.Single(x => x.TenantId == c.Comum.Id).Entra);
        }
        finally
        {
            await LimparAsync(db, c);
        }
    }

    [Fact]
    public async Task Troca_de_plano_antes_da_data_cancela_e_operador_cancela_o_lote()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var c = await MontarAsync(db, sp, Guid.NewGuid().ToString("N"));
        var gateway = new GatewayFalso();
        var servico = Servico(sp, gateway);
        var data = DataBrasilia.Hoje().AddDays(35);
        try
        {
            // Lote 1: cancelado pelo operador antes da data.
            var lote1 = await servico.AgendarAsync(c.Plano.Id, new AgendarReajusteRequest(data, null), null, CancellationToken.None);
            Assert.Equal(1, await servico.CancelarLoteAsync(lote1.LoteId, null, CancellationToken.None));
            await Assert.ThrowsAsync<RegraNegocioException>(() => servico.CancelarLoteAsync(lote1.LoteId, null, CancellationToken.None));

            // Lote 2: a conta troca de plano antes da data → o job cancela, não aplica.
            var lote2 = await servico.AgendarAsync(c.Plano.Id, new AgendarReajusteRequest(data, null), null, CancellationToken.None);
            await db.Assinaturas.Where(a => a.Id == c.AssComum.Id).ExecuteUpdateAsync(x => x.SetProperty(a => a.Plano, Planos.CodigoEmpresa));
            await db.ReajustesAssinatura.Where(r => r.LoteId == lote2.LoteId).ExecuteUpdateAsync(x => x.SetProperty(r => r.AplicarEm, DataBrasilia.Hoje()));
            db.ChangeTracker.Clear();

            var resultado = await servico.ExecutarAsync(CancellationToken.None);
            Assert.Equal(1, resultado.Cancelados);
            Assert.Empty(gateway.Alteracoes);
            var cancelado = await db.ReajustesAssinatura.AsNoTracking().SingleAsync(r => r.LoteId == lote2.LoteId);
            Assert.Equal(StatusReajusteAssinatura.Cancelado, cancelado.Status);
            Assert.Contains("trocou de plano", cancelado.Detalhe);

            var lista = await servico.ListarAsync(c.Plano.Id, CancellationToken.None);
            Assert.Equal(2, lista.Count);
            Assert.All(lista, r => Assert.Equal(StatusReajusteAssinatura.Cancelado, r.Status));
        }
        finally
        {
            await LimparAsync(db, c);
        }
    }
}
