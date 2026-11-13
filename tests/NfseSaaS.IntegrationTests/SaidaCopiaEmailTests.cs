using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// RC1 com o banco real: o link do rodapé (assinado), o pedido com
/// confirmação pelo próprio e-mail, o principal que nunca sai, o histórico,
/// o aviso ao fornecedor e o link no e-mail da nota só quando há cópia.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class SaidaCopiaEmailTests
{
    private readonly AplicacaoTesteFactory _factory;

    public SaidaCopiaEmailTests(AplicacaoTesteFactory factory)
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

    private static string CodigoDoEmail(ItemFilaEmail item)
    {
        var m = Regex.Match(item.CorpoHtml, @"/copia/confirmar\?c=([^""&]+)");
        Assert.True(m.Success, "o e-mail de confirmação deveria ter o link");
        return Uri.UnescapeDataString(WebUtility.HtmlDecode(m.Groups[1].Value));
    }

    [Fact]
    public async Task Copia_sai_com_confirmacao_e_principal_nunca_sai()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Saida " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();

        try
        {
            sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);
            db.TenantIdOverrideDeSistema = conta.Id;
            var empresa = new Empresa { Cnpj = s[..14], RazaoSocial = "Empresa " + s, NomeFantasia = "LFMS", Email = "fornecedor@teste.com" };
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
                EmpresaId = empresa.Id, ClienteId = cliente.Id, NumeroDps = 7, SerieDps = "1", ValorServico = 100, NumeroNfse = "58",
                DataCompetencia = new DateOnly(2026, 11, 1), Status = NfseStatus.Autorizada, TipoAmbiente = TipoAmbiente.Homologacao
            };
            db.NotasFiscais.Add(nota);
            await db.SaveChangesAsync();
            db.TenantIdOverrideDeSistema = null;

            // Como na página pública: outro escopo, sem conta logada.
            using var escopoPublico = _factory.Services.CreateScope();
            var spPublico = escopoPublico.ServiceProvider;
            var dbPublico = spPublico.GetRequiredService<AppDbContext>();
            var link = spPublico.GetRequiredService<LinkSaidaCopiaService>();
            var fila = new FilaFalsa();
            var servico = new SaidaCopiaEmailService(dbPublico, link, new EmailQueueService(dbPublico, fila), spPublico.GetRequiredService<IAuditLogWriter>());
            var servicoDaConta = new SaidaCopiaEmailService(db, sp.GetRequiredService<LinkSaidaCopiaService>(), new EmailQueueService(db, new FilaFalsa()), sp.GetRequiredService<IAuditLogWriter>());

            // Link do rodapé: assinado; adulterado não vale.
            var url = link.Gerar(conta.Id, nota.Id);
            Assert.Contains("/copia/sair?t=", url);
            var token = Uri.UnescapeDataString(url[(url.IndexOf("?t=", StringComparison.Ordinal) + 3)..]);
            Assert.Equal((conta.Id, nota.Id), link.Ler(token));
            Assert.Null(link.Ler(token[..^3] + "abc"));
            Assert.Null(link.Ler(null));
            Assert.Equal("LFMS", (await servico.AbrirAsync(token, CancellationToken.None))!.Empresa);
            Assert.Null(await servico.AbrirAsync("lixo", CancellationToken.None));
            await Assert.ThrowsAsync<RegraNegocioException>(() => servico.PedirAsync("lixo", "contador@teste.com", "1.1.1.1", CancellationToken.None));

            // Principal: nunca sai, nada é enviado.
            Assert.Equal(ResultadoPedidoSaidaCopia.Principal, await servico.PedirAsync(token, " Tomador@Teste.com ", null, CancellationToken.None));
            // Quem não é cópia: resposta neutra e nenhum e-mail.
            Assert.Equal(ResultadoPedidoSaidaCopia.ConfirmacaoEnviada, await servico.PedirAsync(token, "estranho@teste.com", null, CancellationToken.None));
            Assert.Empty(fila.Itens);

            // Cópia: confirmação vai para o próprio endereço; nada muda ainda.
            Assert.Equal(ResultadoPedidoSaidaCopia.ConfirmacaoEnviada, await servico.PedirAsync(token, "Contador@teste.com", "10.0.0.1", CancellationToken.None));
            var confirmacao = Assert.Single(fila.Itens);
            Assert.Equal("contador@teste.com", confirmacao.Destinatario);
            var codigo = CodigoDoEmail(confirmacao);
            Assert.Equal("financeiro@teste.com;contador@teste.com",
                await db.Clientes.AsNoTracking().Where(c => c.Id == cliente.Id).Select(c => c.EmailsCopia).SingleAsync());
            var pendente = await db.SaidasCopiaEmail.AsNoTracking().SingleAsync(x => x.ClienteId == cliente.Id);
            Assert.Equal(StatusSaidaCopia.Pendente, pendente.Status);
            Assert.NotEqual(codigo, pendente.CodigoHash); // só o hash fica no banco
            Assert.Equal("10.0.0.1", pendente.IpPedido);

            // Página de confirmação só mostra (GET não confirma).
            var tela = await servico.ObterConfirmacaoAsync(codigo, CancellationToken.None);
            Assert.NotNull(tela);
            Assert.Equal("co***@teste.com", tela!.EmailMascarado);
            Assert.False(tela.Confirmada);
            Assert.False(tela.Expirada);
            Assert.Null(await servico.ObterConfirmacaoAsync("codigo-errado", CancellationToken.None));
            await Assert.ThrowsAsync<RegraNegocioException>(() => servico.ConfirmarAsync("codigo-errado", null, CancellationToken.None));

            // Confirmar: sai das cópias, histórico, auditoria e aviso ao fornecedor.
            var antesDoAviso = fila.Itens.Count;
            var feito = await servico.ConfirmarAsync(codigo, "10.0.0.2", CancellationToken.None);
            Assert.True(feito.Confirmada);
            db.ChangeTracker.Clear();
            Assert.Equal("financeiro@teste.com",
                await db.Clientes.AsNoTracking().Where(c => c.Id == cliente.Id).Select(c => c.EmailsCopia).SingleAsync());
            var confirmada = await db.SaidasCopiaEmail.AsNoTracking().SingleAsync(x => x.ClienteId == cliente.Id);
            Assert.Equal(StatusSaidaCopia.Confirmada, confirmada.Status);
            Assert.NotNull(confirmada.ConfirmadoEm);
            Assert.Equal("10.0.0.2", confirmada.IpConfirmacao);
            Assert.True(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.TenantId == conta.Id && a.Operacao == "SaidaCopiaConfirmada"));
            Assert.True(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.TenantId == conta.Id && a.Operacao == "PedidoSaidaCopia"));
            Assert.Contains(fila.Itens.Skip(antesDoAviso), i => i.Destinatario == "fornecedor@teste.com" && i.Assunto.Contains("contador@teste.com"));

            // Confirmar de novo: não muda nada nem avisa outra vez.
            var avisos = fila.Itens.Count;
            Assert.True((await servico.ConfirmarAsync(codigo, null, CancellationToken.None)).Confirmada);
            Assert.Equal(avisos, fila.Itens.Count);
            Assert.True((await servico.ObterConfirmacaoAsync(codigo, CancellationToken.None))!.Confirmada);

            // Histórico no cadastro do cliente (com o número da nota).
            var historico = await servicoDaConta.ListarDoClienteAsync(cliente.Id, CancellationToken.None);
            var item = Assert.Single(historico);
            Assert.Equal("contador@teste.com", item.Email);
            Assert.Equal("58", item.NumeroNota);

            // Já saiu: novo pedido é neutro e não manda nada.
            var antes = fila.Itens.Count;
            Assert.Equal(ResultadoPedidoSaidaCopia.ConfirmacaoEnviada, await servico.PedirAsync(token, "contador@teste.com", null, CancellationToken.None));
            Assert.Equal(antes, fila.Itens.Count);

            // Confirmação vencida: não tira ninguém.
            Assert.Equal(ResultadoPedidoSaidaCopia.ConfirmacaoEnviada, await servico.PedirAsync(token, "financeiro@teste.com", null, CancellationToken.None));
            var codigoVencido = CodigoDoEmail(fila.Itens[^1]);
            await db.SaidasCopiaEmail.Where(x => x.Email == "financeiro@teste.com" && x.ClienteId == cliente.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.ExpiraEm, DateTimeOffset.UtcNow.AddMinutes(-1)));
            dbPublico.ChangeTracker.Clear(); // cada requisição tem o próprio contexto
            Assert.True((await servico.ObterConfirmacaoAsync(codigoVencido, CancellationToken.None))!.Expirada);
            await Assert.ThrowsAsync<RegraNegocioException>(() => servico.ConfirmarAsync(codigoVencido, null, CancellationToken.None));
            Assert.Equal("financeiro@teste.com",
                await db.Clientes.AsNoTracking().Where(c => c.Id == cliente.Id).Select(c => c.EmailsCopia).SingleAsync());

            // Limite: no máximo 3 pedidos por e-mail e cliente em 24 horas.
            for (var i = 0; i < 4; i++)
                await servico.PedirAsync(token, "financeiro@teste.com", null, CancellationToken.None);
            Assert.Equal(RegrasSaidaCopia.MaximoPedidosPorDia,
                await db.SaidasCopiaEmail.AsNoTracking().CountAsync(x => x.ClienteId == cliente.Id && x.Email == "financeiro@teste.com"));
        }
        finally
        {
            db.ChangeTracker.Clear();
            db.TenantIdOverrideDeSistema = null;
            await db.SaidasCopiaEmail.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.EmailLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.NotasFiscais.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Clientes.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Email_da_nota_tem_o_link_so_com_copia_e_o_modelo_pode_tirar()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Rodape " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();

        try
        {
            sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);
            db.TenantIdOverrideDeSistema = conta.Id;
            var empresa = new Empresa { Cnpj = s[..14], RazaoSocial = "Empresa " + s };
            db.Empresas.Add(empresa);
            await db.SaveChangesAsync();
            var comCopia = new Cliente { EmpresaId = empresa.Id, Nome = "Com", Apelido = "C" + s[..6], CpfCnpj = s[..11], Email = "a@teste.com", EmailsCopia = "b@teste.com" };
            var semCopia = new Cliente { EmpresaId = empresa.Id, Nome = "Sem", Apelido = "S" + s[..6], CpfCnpj = s[1..12], Email = "c@teste.com" };
            db.Clientes.AddRange(comCopia, semCopia);
            await db.SaveChangesAsync();
            Nfse Nota(Cliente c, int n) => new()
            {
                EmpresaId = empresa.Id, ClienteId = c.Id, NumeroDps = n, SerieDps = "1", ValorServico = 100,
                DataCompetencia = new DateOnly(2026, 11, 1), Status = NfseStatus.Autorizada, TipoAmbiente = TipoAmbiente.Homologacao
            };
            var notaCom = Nota(comCopia, 1);
            var notaSem = Nota(semCopia, 2);
            db.NotasFiscais.AddRange(notaCom, notaSem);
            await db.SaveChangesAsync();
            db.TenantIdOverrideDeSistema = null;

            var fila = new FilaFalsa();
            var enviar = new EnviarNfsePorEmailUseCase(db, new EmailQueueService(db, fila), sp.GetRequiredService<IAuditLogWriter>(),
                sp.GetRequiredService<NfseSaaS.Application.UseCases.EmailNota.IModeloEmailNotaUseCase>(), sp.GetRequiredService<ILinkSaidaCopia>(), sp.GetRequiredService<NfseSaaS.Application.UseCases.EmailNota.ILinkConfirmacaoRecebimento>());

            await enviar.ExecutarAsync(notaCom.Id, automatico: false, texto: null, CancellationToken.None);
            await enviar.ExecutarAsync(notaSem.Id, automatico: false, texto: null, CancellationToken.None);
            Assert.Contains("/copia/sair?t=", fila.Itens[0].CorpoHtml);
            Assert.DoesNotContain("/copia/sair", fila.Itens[1].CorpoHtml);

            // Modelo com a linha desligada: sem link mesmo com cópia.
            db.TenantIdOverrideDeSistema = conta.Id;
            var modelo = new ModeloEmailNota { Nome = "Sem link", Assunto = "Nota", Mensagem = "Oi", Padrao = true, OcultarSaidaCopias = true };
            db.ModelosEmailNota.Add(modelo);
            await db.SaveChangesAsync();
            db.TenantIdOverrideDeSistema = null;
            await enviar.ExecutarAsync(notaCom.Id, automatico: false, texto: null, CancellationToken.None);
            Assert.DoesNotContain("/copia/sair", fila.Itens[^1].CorpoHtml);
        }
        finally
        {
            db.ChangeTracker.Clear();
            db.TenantIdOverrideDeSistema = null;
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
