using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// E-mail da nota ao tomador com o banco real: o automático só sai com o
/// envio ligado no cliente; o EmailLog guarda as cópias (Cc/Cco) para o
/// envio e para o "Reenviar"; o botão da tela manda mesmo com o envio
/// automático desligado.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class EnvioNfseCopiasTests
{
    private readonly AplicacaoTesteFactory _factory;

    public EnvioNfseCopiasTests(AplicacaoTesteFactory factory)
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
    public async Task Automatico_respeita_o_cadastro_e_o_log_guarda_as_copias()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Copias " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();

        try
        {
            sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);
            db.TenantIdOverrideDeSistema = conta.Id;
            var empresa = new Empresa { Cnpj = s[..14], RazaoSocial = "Empresa " + s };
            db.Empresas.Add(empresa);
            await db.SaveChangesAsync();
            var cliente = new Cliente
            {
                EmpresaId = empresa.Id, Nome = "Tomador", Apelido = "T" + s[..6], CpfCnpj = s[..11],
                Email = "tomador@teste.com", EmailsCopia = "financeiro@teste.com;contador@teste.com", CopiaOculta = true
            };
            db.Clientes.Add(cliente);
            await db.SaveChangesAsync();
            var nota = new Nfse
            {
                EmpresaId = empresa.Id, ClienteId = cliente.Id, NumeroDps = 1, SerieDps = "1", ValorServico = 100,
                DataCompetencia = new DateOnly(2026, 11, 1), Status = NfseStatus.Autorizada, TipoAmbiente = TipoAmbiente.Homologacao
            };
            db.NotasFiscais.Add(nota);
            await db.SaveChangesAsync();
            db.TenantIdOverrideDeSistema = null;

            var fila = new FilaFalsa();
            var enviar = new EnviarNfsePorEmailUseCase(db, new EmailQueueService(db, fila), sp.GetRequiredService<IAuditLogWriter>(), sp.GetRequiredService<NfseSaaS.Application.UseCases.EmailNota.IModeloEmailNotaUseCase>(), sp.GetRequiredService<NfseSaaS.Application.UseCases.Clientes.ILinkSaidaCopia>());

            // Padrão (envio desligado): o automático não manda nada.
            Assert.False(cliente.EnviarNfsePorEmail);
            var desligado = await enviar.ExecutarAsync(nota.Id, automatico: true, texto: null, CancellationToken.None);
            Assert.False(desligado.Enfileirado);
            Assert.Empty(fila.Itens);

            // Botão da tela: manda mesmo desligado, com as cópias.
            var manual = await enviar.ExecutarAsync(nota.Id, automatico: false, texto: null, CancellationToken.None);
            Assert.True(manual.Enfileirado);
            Assert.Equal(new[] { "financeiro@teste.com", "contador@teste.com" }, manual.Copias);

            // Ligado: o automático manda.
            await db.Clientes.Where(c => c.Id == cliente.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.EnviarNfsePorEmail, true));
            var ligado = await enviar.ExecutarAsync(nota.Id, automatico: true, texto: null, CancellationToken.None);
            Assert.True(ligado.Enfileirado);
            Assert.Equal(2, fila.Itens.Count);

            var logs = await db.EmailLogs.AsNoTracking().Where(e => e.NfseId == nota.Id).ToListAsync();
            Assert.Equal(2, logs.Count);
            Assert.All(logs, l =>
            {
                Assert.Equal("tomador@teste.com", l.Destinatario);
                Assert.Equal("financeiro@teste.com;contador@teste.com", l.Copias);
                Assert.True(l.CopiasOcultas);
            });
        }
        finally
        {
            db.ChangeTracker.Clear();
            db.TenantIdOverrideDeSistema = null;
            await db.EmailLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.NotasFiscais.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Clientes.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }
}
