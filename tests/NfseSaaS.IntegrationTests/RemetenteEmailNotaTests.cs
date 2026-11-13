using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// O remetente que o cliente vê: "RHSENSO via …" (nome fantasia do cadastro,
/// primeira porção quando longo) — também no "Mandar um teste para mim" do
/// modelo, que não tem nota. Os outros e-mails do sistema seguem como antes.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class RemetenteEmailNotaTests
{
    private readonly AplicacaoTesteFactory _factory;

    public RemetenteEmailNotaTests(AplicacaoTesteFactory factory)
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
    public async Task Teste_do_modelo_sai_com_o_nome_fantasia_da_empresa()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Remetente " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();

        try
        {
            db.TenantIdOverrideDeSistema = conta.Id;
            db.Empresas.Add(new Empresa
            {
                Cnpj = s[..14], RazaoSocial = "LFMS - CONSULTORIA E DESENVOLVIMENTO DE SISTEMAS LTDA",
                NomeFantasia = "RHSENSO - SISTEMAS E CONSULTORIA EM RECURSOS HUMANOS"
            });
            await db.SaveChangesAsync();

            var fila = new FilaFalsa();
            var filaServico = new EmailQueueService(db, fila);
            var teste = $"teste-{s[..8]}@teste.com";
            var outro = $"outro-{s[..8]}@teste.com";
            // Como o "Mandar um teste para mim": tipo da nota, sem nota.
            await filaServico.EnfileirarAsync(new EmailParaEnfileirar(TipoEmailCatalogo.NfseTomador, null, teste, "[Teste do modelo] Nota", "<p>Oi</p>"), CancellationToken.None);
            await filaServico.EnfileirarAsync(new EmailParaEnfileirar(TipoEmailCatalogo.EsqueciSenha, null, outro, "Redefinição", "<p>Oi</p>"), CancellationToken.None);
            await db.SaveChangesAsync();
            db.TenantIdOverrideDeSistema = null;

            var despacho = new EmailDispatchHostedService(fila, _factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<EmailDispatchHostedService>.Instance);
            foreach (var item in fila.Itens)
                await despacho.ProcessarAsync(item, CancellationToken.None);

            var plataforma = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailOptions>>().Value.RemetenteNome;
            Assert.Equal($"RHSENSO via {plataforma}", EmailSenderDosTestes.Ultimas[teste].RemetenteNome);
            // E-mail que não é da nota: continua só com o nome da plataforma (sem mensagem completa).
            Assert.False(EmailSenderDosTestes.Ultimas.ContainsKey(outro));
            Assert.All(await db.EmailLogs.IgnoreQueryFilters().AsNoTracking().Where(e => e.TenantId == conta.Id).ToListAsync(),
                l => Assert.Equal(StatusEmailLog.Enviado, l.Status));
        }
        finally
        {
            db.ChangeTracker.Clear();
            db.TenantIdOverrideDeSistema = null;
            await db.EmailLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }
}
