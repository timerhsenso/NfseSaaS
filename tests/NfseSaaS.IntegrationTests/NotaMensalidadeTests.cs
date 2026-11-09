using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Cobranca;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// N1 com o banco real e a emissão falsa: pagamento confirmado vira nota da
/// conta emissora para a conta que pagou; não duplica; estorno antes da
/// nota não emite; falha fica para a próxima passada; desligado não faz nada.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class NotaMensalidadeTests
{
    private readonly AplicacaoTesteFactory _factory;

    public NotaMensalidadeTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class EmissaoFalsa : IEmitirNfseUseCase
    {
        public List<EmitirNfseRequest> Pedidos { get; } = [];
        public bool Falhar { get; set; }

        public Task<EmitirNfseResult> ExecutarAsync(EmitirNfseRequest request, CancellationToken cancellationToken)
        {
            Pedidos.Add(request);
            return Task.FromResult(Falhar
                ? new EmitirNfseResult(Guid.NewGuid(), false, null, null, "E999", "SEFIN fora do ar")
                : new EmitirNfseResult(Guid.NewGuid(), true, "1", "CHAVE", null, null));
        }
    }

    private sealed record Montagem(Tenant Emissora, Empresa Lfms, Servico Servico, Tenant Pagadora, string EmailAdmin);

    private static NotasMensalidadeService Servico(IServiceProvider sp, Montagem m, EmissaoFalsa emissao, bool ligado = true) =>
        new(sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<ICurrentTenant>(), emissao,
            Options.Create(ligado ? new NotaMensalidadeOptions { EmpresaId = m.Lfms.Id, ServicoId = m.Servico.Id } : new NotaMensalidadeOptions()),
            Options.Create(new CobrancaOptions { ContasPiloto = [m.Pagadora.Id] }),
            NullLogger<NotasMensalidadeService>.Instance);

    private static async Task<Montagem> MontarAsync(AppDbContext db, string s)
    {
        var emissora = new Tenant { Documento = s[..14], RazaoSocial = "LFMS " + s };
        var pagadora = new Tenant { Documento = s[14..28], RazaoSocial = "Padaria " + s, NomeFantasia = "Padaria Sol" };
        db.Tenants.AddRange(emissora, pagadora);
        await db.SaveChangesAsync();

        db.TenantIdOverrideDeSistema = emissora.Id;
        var lfms = new Empresa { Cnpj = emissora.Documento, RazaoSocial = emissora.RazaoSocial };
        db.Empresas.Add(lfms);
        await db.SaveChangesAsync();
        var servico = new Servico { EmpresaId = lfms.Id, Descricao = "Licença de uso", CodigoTributacaoNacional = "010501" };
        db.Servicos.Add(servico);
        await db.SaveChangesAsync();

        db.TenantIdOverrideDeSistema = pagadora.Id;
        db.Empresas.Add(new Empresa
        {
            Cnpj = pagadora.Documento, RazaoSocial = pagadora.RazaoSocial, CodigoMunicipio = "2927408", Cep = "40000000",
            Logradouro = "Rua A", Numero = "10", Bairro = "Centro", Uf = "BA"
        });
        var grupo = new Grupo { Nome = "Admin " + s, EhAdministrador = true };
        db.Grupos.Add(grupo);
        await db.SaveChangesAsync();
        var email = $"adm{s}@t.com";
        db.Users.Add(new ApplicationUser { TenantId = pagadora.Id, UserName = email, Email = email, GrupoId = grupo.Id });
        await db.SaveChangesAsync();
        db.TenantIdOverrideDeSistema = null;
        db.ChangeTracker.Clear();
        return new Montagem(emissora, lfms, servico, pagadora, email);
    }

    private static async Task EventoAsync(AppDbContext db, Guid tenantId, string tipo, string pagamentoId, decimal valor = 49.90m)
    {
        db.EventosCobranca.Add(new EventoCobranca
        {
            EventoId = "evt_" + Guid.NewGuid().ToString("N"), Tipo = tipo, AsaasPagamentoId = pagamentoId, TenantId = tenantId, Processado = true,
            Payload = "{\"event\":\"" + tipo + "\",\"payment\":{\"id\":\"" + pagamentoId + "\",\"value\":"
                      + valor.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"paymentDate\":\"2026-11-07\"}}"
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task LimparAsync(AppDbContext db, Montagem m, IEnumerable<string> pagamentos)
    {
        db.ChangeTracker.Clear();
        var ids = pagamentos.ToList();
        await db.NotasMensalidade.Where(x => ids.Contains(x.AsaasPagamentoId)).ExecuteDeleteAsync();
        await db.EventosCobranca.Where(x => x.AsaasPagamentoId != null && ids.Contains(x.AsaasPagamentoId)).ExecuteDeleteAsync();
        foreach (var t in new[] { m.Emissora.Id, m.Pagadora.Id })
        {
            await db.Clientes.IgnoreQueryFilters().Where(x => x.TenantId == t).ExecuteDeleteAsync();
            await db.Servicos.IgnoreQueryFilters().Where(x => x.TenantId == t).ExecuteDeleteAsync();
            await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == t).ExecuteDeleteAsync();
            await db.Users.Where(x => x.TenantId == t).ExecuteDeleteAsync();
            await db.Grupos.IgnoreQueryFilters().Where(x => x.TenantId == t).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == t).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Pagamento_confirmado_vira_nota_uma_vez_com_o_tomador_criado()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var m = await MontarAsync(db, s);
        var pay = "pay_ok_" + s[..10];
        var emissao = new EmissaoFalsa();

        try
        {
            await EventoAsync(db, m.Pagadora.Id, "PAYMENT_CONFIRMED", pay);
            // O RECEIVED do mesmo pagamento (cartão manda os dois) não gera outra nota.
            await EventoAsync(db, m.Pagadora.Id, "PAYMENT_RECEIVED", pay);

            // Desligado: nada.
            var desligado = await Servico(sp, m, emissao, ligado: false).ExecutarAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            Assert.True(desligado.Desligado);
            Assert.False(await db.NotasMensalidade.AnyAsync(n => n.AsaasPagamentoId == pay));

            await Servico(sp, m, emissao).ExecutarAsync(DateTimeOffset.UtcNow, CancellationToken.None);

            var pedido = Assert.Single(emissao.Pedidos);
            Assert.Equal(m.Lfms.Id, pedido.EmpresaId);
            Assert.Equal(m.Servico.Id, pedido.ServicoId);
            Assert.Equal(49.90m, pedido.ValorServico);
            Assert.Equal(new DateOnly(2026, 11, 7), pedido.DataCompetencia);
            Assert.Equal(RegrasNotaMensalidade.ChaveIdempotencia(pay), pedido.IdempotencyKey);
            Assert.Contains(pay, pedido.DescricaoServico);

            var nota = await db.NotasMensalidade.AsNoTracking().SingleAsync(n => n.AsaasPagamentoId == pay);
            Assert.Equal(StatusNotaMensalidade.Emitida, nota.Status);
            Assert.Equal(m.Pagadora.Id, nota.TenantId);
            Assert.NotNull(nota.NfseId);

            // Tomador criado na conta emissora, com o endereço da empresa da pagadora e o e-mail do admin.
            var cliente = await db.Clientes.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == pedido.ClienteId);
            Assert.Equal(m.Emissora.Id, cliente.TenantId);
            Assert.Equal(m.Pagadora.Documento, cliente.CpfCnpj);
            Assert.Equal("2927408", cliente.CodigoMunicipio);
            Assert.Equal("Padaria Sol", cliente.Apelido);
            Assert.Equal(m.EmailAdmin, cliente.Email);
            Assert.True(cliente.EnviarNfsePorEmail);

            // Segunda passada: nada novo.
            var r2 = await Servico(sp, m, emissao).ExecutarAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            Assert.Single(emissao.Pedidos);
            Assert.Equal(0, r2.Registradas);

            // Pagamento desfeito depois da nota: só marca o aviso ao operador (uma vez).
            await EventoAsync(db, m.Pagadora.Id, "PAYMENT_REFUNDED", pay);
            await Servico(sp, m, emissao).ExecutarAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            nota = await db.NotasMensalidade.AsNoTracking().SingleAsync(n => n.AsaasPagamentoId == pay);
            Assert.Equal(StatusNotaMensalidade.Emitida, nota.Status);
            Assert.NotNull(nota.EstornoAvisadoEm);
        }
        finally
        {
            await LimparAsync(db, m, [pay]);
        }
    }

    [Fact]
    public async Task Falha_tenta_de_novo_estorno_antes_nao_emite_e_a_propria_conta_fica_de_fora()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var m = await MontarAsync(db, s);
        var falha = "pay_er_" + s[..10];
        var estornado = "pay_es_" + s[..10];
        var proprio = "pay_pr_" + s[..10];
        var emissao = new EmissaoFalsa { Falhar = true };

        try
        {
            await EventoAsync(db, m.Pagadora.Id, "PAYMENT_CONFIRMED", falha);
            await EventoAsync(db, m.Pagadora.Id, "PAYMENT_CONFIRMED", estornado);
            await EventoAsync(db, m.Pagadora.Id, "PAYMENT_REFUNDED", estornado);
            await EventoAsync(db, m.Emissora.Id, "PAYMENT_CONFIRMED", proprio);

            await Servico(sp, m, emissao).ExecutarAsync(DateTimeOffset.UtcNow, CancellationToken.None);

            Assert.False(await db.NotasMensalidade.AnyAsync(n => n.AsaasPagamentoId == proprio));
            var ignorada = await db.NotasMensalidade.AsNoTracking().SingleAsync(n => n.AsaasPagamentoId == estornado);
            Assert.Equal(StatusNotaMensalidade.Ignorada, ignorada.Status);
            Assert.DoesNotContain(emissao.Pedidos, p => p.IdempotencyKey == RegrasNotaMensalidade.ChaveIdempotencia(estornado));

            var comErro = await db.NotasMensalidade.AsNoTracking().SingleAsync(n => n.AsaasPagamentoId == falha);
            Assert.Equal(StatusNotaMensalidade.Erro, comErro.Status);
            Assert.Equal(1, comErro.Tentativas);
            Assert.Contains("E999", comErro.UltimoErro);

            // Próxima passada com a SEFIN de volta: emite.
            emissao.Falhar = false;
            await Servico(sp, m, emissao).ExecutarAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            var emitida = await db.NotasMensalidade.AsNoTracking().SingleAsync(n => n.AsaasPagamentoId == falha);
            Assert.Equal(StatusNotaMensalidade.Emitida, emitida.Status);
            Assert.Equal(2, emitida.Tentativas);
            Assert.Null(emitida.UltimoErro);
        }
        finally
        {
            await LimparAsync(db, m, [falha, estornado, proprio]);
        }
    }
}
