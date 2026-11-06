using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Common;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.Persistence.Seeders;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// D3: o EmailLog nunca guarda token de acesso; o envio usa o corpo
/// verdadeiro; depois de um reinício, o e-mail com token não é mandado
/// com link quebrado (fica Falhou, com a orientação de gerar outro).
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class MascaramentoEmailLogTests
{
    private readonly AplicacaoTesteFactory _factory;

    public MascaramentoEmailLogTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class FilaFalsa : IBackgroundEmailQueue
    {
        public List<ItemFilaEmail> Itens { get; } = new();
        public void Enfileirar(ItemFilaEmail item) => Itens.Add(item);
        public async IAsyncEnumerable<ItemFilaEmail> LerTodosAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    [Fact]
    public async Task Log_guarda_sem_token_envio_leva_o_token_e_reinicio_nao_manda_link_quebrado()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Mascara " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        const string token = "CfDJ8+segredo/abc==";
        var corpo = $"<a href=\"https://app/Account/RedefinirSenha?token={Uri.EscapeDataString(token)}\">x</a> Token: {token}";
        var fila = new FilaFalsa();
        var servico = new EmailQueueService(db, fila);

        try
        {
            var id = await servico.EnfileirarAsync(
                new EmailParaEnfileirar(TipoEmailCatalogo.EsqueciSenha, null, $"u{s}@teste.com", "Redefinição", corpo, Segredos: new[] { token }),
                CancellationToken.None);

            // O envio (fila em memória) leva o token; o log não.
            Assert.Contains(Uri.EscapeDataString(token), Assert.Single(fila.Itens).CorpoHtml);
            var log = await db.EmailLogs.AsNoTracking().SingleAsync(e => e.Id == id);
            Assert.DoesNotContain("segredo", log.CorpoHtml);
            Assert.Contains(MascaramentoEmail.Oculto, log.CorpoHtml);

            // Um e-mail comum, ainda Pendente, para comparar.
            var comum = await servico.EnfileirarAsync(
                new EmailParaEnfileirar(TipoEmailCatalogo.AvisoCobranca, null, $"u{s}@teste.com", "Aviso", "<p>oi</p>"), CancellationToken.None);

            // "Reinício": o recuperador não reenvia o que tinha token; reenfileira o comum.
            await EmailPendenteRecuperador.ReenfileirarPendentesAsync(_factory.Services);

            db.ChangeTracker.Clear();
            var depois = await db.EmailLogs.AsNoTracking().SingleAsync(e => e.Id == id);
            Assert.Equal(StatusEmailLog.Falhou, depois.Status);
            Assert.Contains("Gere um novo", depois.ErroDetalhe);
            // O comum volta para a fila de envio (aqui sem SMTP ele pode até
            // falhar no envio, mas nunca com a orientação de link).
            var comumDepois = await db.EmailLogs.AsNoTracking().SingleAsync(e => e.Id == comum);
            Assert.DoesNotContain("Gere um novo", comumDepois.ErroDetalhe ?? string.Empty);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.EmailLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }
}
