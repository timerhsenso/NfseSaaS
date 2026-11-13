using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.DocumentosEmpresa;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Documents;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// DOC2 com o banco real: o cliente marca os documentos exigidos; o envio
/// automático segue a escolha do cadastro (sem o vencido, com ele, ou
/// segurando o e-mail); pela tela o vencido pede "mandar assim mesmo?";
/// o lote avisa; e os PDFs entram como anexo na hora do envio.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class DocumentosExigidosTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n%documento exigido\n");

    private readonly AplicacaoTesteFactory _factory;

    public DocumentosExigidosTests(AplicacaoTesteFactory factory)
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

    private sealed class UsuarioFalso(Guid id) : ICurrentUser
    {
        public Guid? UserId { get; } = id;
        public string? IpAddress => "127.0.0.1";
    }

    [Fact]
    public async Task Exigidos_no_cliente_valem_no_envio_automatico_na_tela_e_no_lote()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var pasta = Path.Combine(Path.GetTempPath(), "doc2-" + s);
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Doc2 " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();

        try
        {
            var tenantAtual = sp.GetRequiredService<ICurrentTenant>();
            tenantAtual.DefinirTenantIdDeSistema(conta.Id);
            var hoje = DataBrasilia.Hoje();

            var grupo = new Grupo { Nome = "Administrador", EhAdministrador = true };
            db.Grupos.Add(grupo);
            var empresa = new Empresa { Cnpj = s[..14], RazaoSocial = "Alfa LTDA", Email = "alfa@alfa.com", TipoAmbiente = TipoAmbiente.Producao };
            db.Empresas.Add(empresa);
            await db.SaveChangesAsync();
            var admin = new ApplicationUser { TenantId = conta.Id, UserName = $"adm{s}@t.com", Email = $"adm{s}@t.com", GrupoId = grupo.Id };
            db.Users.Add(admin);
            var cliente = new Cliente
            {
                EmpresaId = empresa.Id, Nome = "Prefeitura Exigente", Apelido = "PREF" + s[..4], CpfCnpj = Guid.NewGuid().ToString("N")[..11],
                Email = "compras@pref.gov.br", EnviarNfsePorEmail = true
            };
            db.Clientes.Add(cliente);

            // Documentos da empresa: FGTS em dia, CNDT vencida; a estadual não existe.
            var store = new DocumentoEmpresaFileStore(Options.Create(new DocumentStorageOptions { BasePath = pasta }));
            var fgts = new DocumentoEmpresa
            {
                EmpresaId = empresa.Id, Tipo = TiposDocumentoSistema.CrfFgts, NomeArquivo = "crf.pdf", TamanhoBytes = Pdf.Length,
                DataEmissao = hoje.AddDays(-5), ValidaAte = hoje.AddDays(25)
            };
            var cndt = new DocumentoEmpresa
            {
                EmpresaId = empresa.Id, Tipo = TiposDocumentoSistema.Cndt, NomeArquivo = "cndt.pdf", TamanhoBytes = Pdf.Length,
                DataEmissao = hoje.AddDays(-200), ValidaAte = hoje.AddDays(-20)
            };
            db.DocumentosEmpresa.AddRange(fgts, cndt);
            await db.SaveChangesAsync();
            await store.SalvarAsync(conta.Id, empresa.Id, fgts.Id, Pdf, CancellationToken.None);
            await store.SalvarAsync(conta.Id, empresa.Id, cndt.Id, Pdf, CancellationToken.None);

            var numero = 0;
            Nfse NovaNota() => new()
            {
                EmpresaId = empresa.Id, ClienteId = cliente.Id, NumeroDps = ++numero, SerieDps = "1", NumeroNfse = (500 + numero).ToString(),
                ValorServico = 1000m, DataCompetencia = new DateOnly(2026, 11, 1), DataEmissao = DateTimeOffset.UtcNow,
                DescricaoServico = "Consultoria", Status = NfseStatus.Autorizada, TipoAmbiente = TipoAmbiente.Producao,
                ChaveAcesso = "DOC2" + s + numero, XmlNfse = "<NFSe><teste/></NFSe>"
            };
            var notas = Enumerable.Range(0, 5).Select(_ => NovaNota()).ToList();
            db.NotasFiscais.AddRange(notas);
            await db.SaveChangesAsync();

            // ----- Cadastro do cliente -----
            var exigidos = sp.GetRequiredService<IDocumentosExigidosClienteUseCase>();
            var opcoes = await exigidos.ObterAsync(cliente.Id, empresa.Id, CancellationToken.None);
            Assert.All(opcoes.Opcoes, o => Assert.False(o.Exigido));
            Assert.Equal(SituacaoDocumentoExigido.Vencido, opcoes.Opcoes.Single(o => o.Tipo == TiposDocumentoSistema.Cndt).Situacao);

            await exigidos.SalvarAsync(cliente.Id, new SalvarDocumentosExigidosRequest(new[]
            {
                new ChaveDocumentoRequest(TiposDocumentoSistema.CrfFgts, null),
                new ChaveDocumentoRequest(TiposDocumentoSistema.Cndt, null),
                new ChaveDocumentoRequest(TiposDocumentoSistema.CndEstadual, null)
            }, AcaoDocumentoPendente.MandarSemOPendente), CancellationToken.None);
            var salvo = await exigidos.ObterAsync(cliente.Id, empresa.Id, CancellationToken.None);
            Assert.Equal(3, salvo.Opcoes.Count(o => o.Exigido));
            Assert.Equal(SituacaoDocumentoExigido.Faltando, salvo.Opcoes.Single(o => o.Tipo == TiposDocumentoSistema.CndEstadual).Situacao);

            var fila = new FilaFalsa();
            var auditoria = sp.GetRequiredService<IAuditLogWriter>();
            var modelos = new ModeloEmailNotaService(db, tenantAtual, new UsuarioFalso(admin.Id), auditoria, new EmailQueueService(db, fila),
                sp.GetRequiredService<IOptions<PlataformaOptions>>(), sp.GetRequiredService<NfseSaaS.Application.UseCases.Recursos.IRecursosConta>());
            var enviar = new EnviarNfsePorEmailUseCase(db, new EmailQueueService(db, fila), auditoria, modelos, sp.GetRequiredService<NfseSaaS.Application.UseCases.Clientes.ILinkSaidaCopia>());
            var envio = new EnvioEmailNfseService(db, enviar, modelos);

            async Task<EmailLog> UltimoEmail(Guid nfseId) =>
                await db.EmailLogs.AsNoTracking().Where(e => e.NfseId == nfseId).OrderByDescending(e => e.CreatedAt).FirstAsync();

            // ----- Automático, padrão: vai o FGTS; CNDT vencida e estadual faltando ficam no alerta -----
            var auto = await enviar.ExecutarAsync(notas[0].Id, automatico: true, texto: null, CancellationToken.None);
            Assert.True(auto.Enfileirado);
            var log0 = await UltimoEmail(notas[0].Id);
            var anexos0 = RegrasDocumentosExigidos.Desserializar(log0.DocumentosAnexos);
            Assert.Equal(fgts.Id, Assert.Single(anexos0).Id);
            Assert.Contains("Vencido, não foi: Certidão trabalhista (CNDT)", log0.AlertaDocumentos);
            Assert.Contains("Faltando", log0.AlertaDocumentos);

            // Os PDFs entram na hora do envio, com o nome do documento.
            var montador = new AnexosNfseEmail(db, sp.GetRequiredService<IObterDanfsePdfUseCase>(), NullLogger<AnexosNfseEmail>.Instance, store);
            var anexos = await montador.MontarAsync(notas[0].Id, log0.DocumentosAnexos, CancellationToken.None);
            Assert.Contains(anexos, a => a.NomeArquivo.EndsWith(".xml"));
            var anexoFgts = Assert.Single(anexos, a => a.NomeArquivo == "Regularidade do FGTS (CRF).pdf");
            Assert.Equal(Pdf, anexoFgts.Conteudo);

            // ----- Automático, "mandar o vencido assim mesmo" -----
            await exigidos.SalvarAsync(cliente.Id, new SalvarDocumentosExigidosRequest(new[]
            {
                new ChaveDocumentoRequest(TiposDocumentoSistema.CrfFgts, null),
                new ChaveDocumentoRequest(TiposDocumentoSistema.Cndt, null)
            }, AcaoDocumentoPendente.MandarMesmoVencido), CancellationToken.None);
            await enviar.ExecutarAsync(notas[1].Id, automatico: true, texto: null, CancellationToken.None);
            var log1 = await UltimoEmail(notas[1].Id);
            Assert.Equal(2, RegrasDocumentosExigidos.Desserializar(log1.DocumentosAnexos).Count);
            Assert.StartsWith("Vencido enviado assim mesmo", log1.AlertaDocumentos);

            // ----- Automático, "não mandar o e-mail": segura e registra -----
            await exigidos.SalvarAsync(cliente.Id, new SalvarDocumentosExigidosRequest(new[]
            {
                new ChaveDocumentoRequest(TiposDocumentoSistema.Cndt, null)
            }, AcaoDocumentoPendente.NaoMandarEmail), CancellationToken.None);
            var segurado = await enviar.ExecutarAsync(notas[2].Id, automatico: true, texto: null, CancellationToken.None);
            Assert.False(segurado.Enfileirado);
            Assert.Contains("não enviado automaticamente", segurado.Motivo);
            Assert.False(await db.EmailLogs.AnyAsync(e => e.NfseId == notas[2].Id));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.EntidadeId == notas[2].Id && a.Operacao == "EmailNotaSeguradoPorDocumento"));

            // ----- Pela tela: vencido marcado pergunta; confirmado, vai -----
            var previa = await envio.PreviaAsync(notas[2].Id, null, CancellationToken.None);
            var docPrevia = Assert.Single(previa.Documentos!);
            Assert.Equal(SituacaoDocumentoExigido.Vencido, docPrevia.Situacao);

            var pergunta = await Assert.ThrowsAsync<RegraNegocioException>(() =>
                enviar.ExecutarAsync(notas[2].Id, automatico: false, new TextoEmailNota(null, null, DocumentosIds: new[] { cndt.Id }), CancellationToken.None));
            Assert.Contains("mandar assim mesmo?", pergunta.Message);

            var confirmado = await enviar.ExecutarAsync(notas[2].Id, automatico: false,
                new TextoEmailNota(null, null, DocumentosIds: new[] { cndt.Id }, EnviarVencidos: true), CancellationToken.None);
            Assert.True(confirmado.Enfileirado);
            Assert.NotNull(confirmado.AlertaDocumentos);
            Assert.Equal(cndt.Id, Assert.Single(RegrasDocumentosExigidos.Desserializar((await UltimoEmail(notas[2].Id)).DocumentosAnexos)).Id);

            // ----- Lote: avisa, e sem marcar o vencido vai sem ele -----
            var previaLote = await envio.PreviaLoteAsync(new EnvioEmailLoteRequest(new[] { notas[3].Id, notas[4].Id }), CancellationToken.None);
            Assert.Equal(2, previaLote.ComDocumentoVencido);
            var resultadoLote = await envio.EnviarLoteAsync(new EnvioEmailLoteRequest(new[] { notas[3].Id, notas[4].Id }), CancellationToken.None);
            Assert.Equal(2, resultadoLote.Enfileiradas);
            var log3 = await UltimoEmail(notas[3].Id);
            Assert.Null(log3.DocumentosAnexos);
            Assert.Contains("Vencido, não foi", log3.AlertaDocumentos);
        }
        finally
        {
            db.ChangeTracker.Clear();
            db.TenantIdOverrideDeSistema = null;
            await db.EmailLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.ModelosEmailNota.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.NotasFiscais.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Clientes.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.DocumentosEmpresa.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Users.Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Grupos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
            if (Directory.Exists(pasta))
                Directory.Delete(pasta, recursive: true);
        }
    }
}
