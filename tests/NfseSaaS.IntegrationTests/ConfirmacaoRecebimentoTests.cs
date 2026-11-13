using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.EmailNota;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// CR1 com o banco real: o campo [Confirmar recebimento] vira link no e-mail
/// da nota; a página pública (sem conta logada) só confirma e-mail que
/// recebeu a nota (principal ou cópia); a lista mostra o envelope verde e
/// filtra; o detalhe lista quem confirmou.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class ConfirmacaoRecebimentoTests
{
    private readonly AplicacaoTesteFactory _factory;

    public ConfirmacaoRecebimentoTests(AplicacaoTesteFactory factory)
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

    private static string? TokenDoEmail(ItemFilaEmail item)
    {
        var m = Regex.Match(item.CorpoHtml, @"/recebimento/confirmar\?t=([^""&]+)");
        return m.Success ? Uri.UnescapeDataString(WebUtility.HtmlDecode(m.Groups[1].Value)) : null;
    }

    [Fact]
    public async Task Destinatario_confirma_e_a_lista_mostra()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Recebimento " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();

        try
        {
            sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);
            db.TenantIdOverrideDeSistema = conta.Id;
            var empresa = new Empresa { Cnpj = s[..14], RazaoSocial = "LFMS " + s, NomeFantasia = "RHSENSO", TipoAmbiente = TipoAmbiente.Producao };
            db.Empresas.Add(empresa);
            await db.SaveChangesAsync();
            var cliente = new Cliente
            {
                EmpresaId = empresa.Id, Nome = "Tomador", Apelido = "T" + s[..6], CpfCnpj = s[..11],
                Email = "tomador@teste.com", EmailsCopia = "financeiro@teste.com", CopiaOculta = true
            };
            db.Clientes.Add(cliente);
            await db.SaveChangesAsync();
            Nfse Nota(int n) => new()
            {
                EmpresaId = empresa.Id, ClienteId = cliente.Id, NumeroDps = n, SerieDps = "1", ValorServico = 344.21m, NumeroNfse = "5" + n,
                DataCompetencia = new DateOnly(2026, 11, 1), Status = NfseStatus.Autorizada, TipoAmbiente = TipoAmbiente.Producao
            };
            var nota = Nota(8);
            var semPedido = Nota(9);
            db.NotasFiscais.AddRange(nota, semPedido);
            await db.SaveChangesAsync();
            db.TenantIdOverrideDeSistema = null;

            var fila = new FilaFalsa();
            var enviar = new EnviarNfsePorEmailUseCase(db, new EmailQueueService(db, fila), sp.GetRequiredService<IAuditLogWriter>(),
                sp.GetRequiredService<IModeloEmailNotaUseCase>(), sp.GetRequiredService<NfseSaaS.Application.UseCases.Clientes.ILinkSaidaCopia>(),
                sp.GetRequiredService<ILinkConfirmacaoRecebimento>());

            // Mensagem com o campo: o e-mail leva o link. Sem o campo: não leva.
            await enviar.ExecutarAsync(nota.Id, automatico: false,
                new TextoEmailNota("Nota [Número da nota]", "Olá.\n\nConfirme aqui - [Confirmar recebimento]"), CancellationToken.None);
            await enviar.ExecutarAsync(semPedido.Id, automatico: false, new TextoEmailNota("Nota", "Olá, sem pedido."), CancellationToken.None);
            var token = TokenDoEmail(fila.Itens[0]);
            Assert.NotNull(token);
            Assert.Contains(">confirmar recebimento</a>", fila.Itens[0].CorpoHtml);
            Assert.Null(TokenDoEmail(fila.Itens[1]));

            // Página pública: outro escopo, sem conta logada.
            using var escopoPublico = _factory.Services.CreateScope();
            var publico = escopoPublico.ServiceProvider.GetRequiredService<IConfirmacaoRecebimentoUseCase>();
            var link = escopoPublico.ServiceProvider.GetRequiredService<ILinkConfirmacaoRecebimento>();

            var pagina = await publico.AbrirAsync(token, CancellationToken.None);
            Assert.NotNull(pagina);
            Assert.Equal("RHSENSO", pagina!.Empresa);
            Assert.Equal("58", pagina.Numero);
            Assert.Empty(pagina.Confirmacoes);
            Assert.Null(await publico.AbrirAsync(token![..^3] + "xyz", CancellationToken.None));
            await Assert.ThrowsAsync<RegraNegocioException>(() => publico.ConfirmarAsync("lixo", "tomador@teste.com", null, CancellationToken.None));
            Assert.Throws<RegraNegocioException>(() => link.Gerar(conta.Id, nota.Id, 0));

            // Quem não recebeu a nota não confirma.
            var fora = await Assert.ThrowsAsync<RegraNegocioException>(() => publico.ConfirmarAsync(token, "estranho@teste.com", null, CancellationToken.None));
            Assert.Equal(RegrasConfirmacaoRecebimento.MensagemForaDaLista, fora.Message);

            // A cópia confirma; de novo não duplica; o principal também confirma.
            var copia = await publico.ConfirmarAsync(token, " Financeiro@Teste.com ", "10.0.0.5", CancellationToken.None);
            Assert.False(copia.JaConfirmado);
            Assert.Equal("fi***@te***.com", copia.EmailMascarado);
            Assert.True((await publico.ConfirmarAsync(token, "financeiro@teste.com", null, CancellationToken.None)).JaConfirmado);
            Assert.False((await publico.ConfirmarAsync(token, "tomador@teste.com", "10.0.0.6", CancellationToken.None)).JaConfirmado);

            var depois = await publico.AbrirAsync(token, CancellationToken.None);
            Assert.Equal(new[] { "fi***@te***.com", "to***@te***.com" }, depois!.Confirmacoes.Select(c => c.EmailMascarado));

            // Conta logada: detalhe com e-mail inteiro, principal/cópia e IP.
            var detalhe = await sp.GetRequiredService<IConfirmacaoRecebimentoUseCase>().ListarDaNotaAsync(nota.Id, CancellationToken.None);
            Assert.Equal(2, detalhe.Count);
            Assert.Equal(("financeiro@teste.com", false, "10.0.0.5"), (detalhe[0].Email, detalhe[0].Principal, detalhe[0].Ip));
            Assert.True(detalhe[1].Principal);

            // Lista: envelope verde e filtros.
            var listar = new ListarNfseUseCase(db);
            var todas = (await listar.ExecutarAsync(new ListarNfseRequest(empresa.Id), CancellationToken.None)).Items;
            var linha = todas.Single(n => n.Id == nota.Id);
            Assert.Equal(2, linha.RecebimentoConfirmacoes);
            Assert.Equal("financeiro@teste.com", linha.RecebimentoConfirmadoPor);
            Assert.NotNull(linha.RecebimentoConfirmadoEm);
            Assert.Equal(0, todas.Single(n => n.Id == semPedido.Id).RecebimentoConfirmacoes);

            var confirmadas = (await listar.ExecutarAsync(new ListarNfseRequest(empresa.Id, Recebimento: "confirmado"), CancellationToken.None)).Items;
            Assert.Equal(new[] { nota.Id }, confirmadas.Select(n => n.Id));
            var pendentes = (await listar.ExecutarAsync(new ListarNfseRequest(empresa.Id, Recebimento: "pendente"), CancellationToken.None)).Items;
            Assert.Equal(new[] { semPedido.Id }, pendentes.Select(n => n.Id));
        }
        finally
        {
            db.ChangeTracker.Clear();
            db.TenantIdOverrideDeSistema = null;
            await db.ConfirmacoesRecebimentoNota.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.EmailLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.NotasFiscais.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.ModelosEmailNota.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Clientes.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }
}
