using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.DocumentosEmpresa;
using NfseSaaS.Application.UseCases.Recursos;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Documents;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// DOC1 com o banco real e uma pasta temporária: versões, histórico,
/// exclusão, bloqueio pelo plano, resumo do painel e o aviso por e-mail
/// (uma vez por faixa e por versão).
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class DocumentosEmpresaTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n%documento de teste\n");

    private readonly AplicacaoTesteFactory _factory;

    public DocumentosEmpresaTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed record Cenario(Tenant Conta, Empresa Empresa, string Pasta);

    private static async Task<Cenario> CriarAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Documentos " + s, Perfil = PerfilConta.Empresa };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        var empresa = new Empresa
        {
            Cnpj = "04747304000178", RazaoSocial = "Empresa " + s, InscricaoMunicipal = "1", CodigoMunicipio = "2919207", Uf = "BA",
            Cep = "42700000", Logradouro = "Rua", Numero = "1", Bairro = "Centro", Telefone = "71999999999", Email = $"doc{s[..8]}@teste.com",
            OpSimpNac = "3", RegApTribSN = "1", RegEspTrib = "0", TribIssqn = "1", CstPisCofins = "00", PercentualTotalTributosSimplesNacional = "6.00"
        };
        db.Empresas.Add(empresa);
        await db.SaveChangesAsync();
        return new Cenario(conta, empresa, Path.Combine(Path.GetTempPath(), "doc1-" + s));
    }

    private static DocumentosEmpresaService Servico(IServiceProvider sp, Cenario c) => new(
        sp.GetRequiredService<AppDbContext>(),
        sp.GetRequiredService<ICurrentTenant>(),
        sp.GetRequiredService<ICurrentUser>(),
        sp.GetRequiredService<IRecursosConta>(),
        new DocumentoEmpresaFileStore(Options.Create(new DocumentStorageOptions { BasePath = c.Pasta })),
        sp.GetRequiredService<IAuditLogWriter>());

    private static async Task LimparAsync(IServiceProvider sp, Cenario c)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        db.ChangeTracker.Clear();
        await db.EmailLogs.IgnoreQueryFilters().Where(x => x.TenantId == c.Conta.Id).ExecuteDeleteAsync();
        await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == c.Conta.Id).ExecuteDeleteAsync();
        await db.DocumentosEmpresa.IgnoreQueryFilters().Where(x => x.TenantId == c.Conta.Id).ExecuteDeleteAsync();
        await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == c.Conta.Id).ExecuteDeleteAsync();
        await db.ExcecoesRecursoConta.Where(x => x.TenantId == c.Conta.Id).ExecuteDeleteAsync();
        await db.Tenants.Where(x => x.Id == c.Conta.Id).ExecuteDeleteAsync();
        if (Directory.Exists(c.Pasta))
            Directory.Delete(c.Pasta, recursive: true);
    }

    private static EnviarDocumentoEmpresaRequest Pedido(Guid empresaId, TipoDocumentoEmpresa tipo, DateOnly? emissao, DateOnly? validade = null, string? nome = null) =>
        new(empresaId, tipo, nome, emissao, validade, null);

    [Fact]
    public async Task Versoes_historico_download_e_exclusao_que_devolve_a_anterior()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var c = await CriarAsync(sp);
        try
        {
            var servico = Servico(sp, c);
            var hoje = DataBrasilia.Hoje();

            // Lista inicial: os tipos fixos aparecem como "não enviado".
            var vazia = await servico.ListarAsync(c.Empresa.Id, CancellationToken.None);
            Assert.True(vazia.EnvioLiberado);
            Assert.Equal(CatalogoDocumentosEmpresa.Fixos.Count(), vazia.Itens.Count);
            Assert.All(vazia.Itens, i => Assert.Null(i.Atual));

            // FGTS sem validade informada: emissão + 30 dias.
            var primeira = await servico.EnviarAsync(Pedido(c.Empresa.Id, TipoDocumentoEmpresa.CrfFgts, hoje.AddDays(-20)),
                new ArquivoDocumentoEmpresa("crf antigo.pdf", Pdf), CancellationToken.None);
            Assert.Equal(hoje.AddDays(10), primeira.ValidaAte);
            Assert.Equal(SituacaoDocumentoEmpresa.VenceEmBreve, primeira.Situacao);
            Assert.True(File.Exists(Path.Combine(c.Pasta, c.Conta.Id.ToString("N"), c.Empresa.Id.ToString("N"), "documentos", $"{primeira.Id:N}.pdf")));

            var segunda = await servico.EnviarAsync(Pedido(c.Empresa.Id, TipoDocumentoEmpresa.CrfFgts, hoje),
                new ArquivoDocumentoEmpresa("crf novo.pdf", Pdf), CancellationToken.None);

            // "Outro": mesmo nome com outra grafia cai no mesmo histórico.
            await servico.EnviarAsync(Pedido(c.Empresa.Id, TipoDocumentoEmpresa.Outro, hoje, nome: "Alvará de funcionamento"),
                new ArquivoDocumentoEmpresa("alvara.pdf", Pdf), CancellationToken.None);
            await servico.EnviarAsync(Pedido(c.Empresa.Id, TipoDocumentoEmpresa.Outro, hoje, nome: "  alvará DE funcionamento "),
                new ArquivoDocumentoEmpresa("alvara2.pdf", Pdf), CancellationToken.None);

            var lista = await servico.ListarAsync(c.Empresa.Id, CancellationToken.None);
            var fgts = lista.Itens.Single(i => i.Tipo == TipoDocumentoEmpresa.CrfFgts);
            Assert.Equal(segunda.Id, fgts.Atual!.Id);
            Assert.Equal(2, fgts.TotalVersoes);
            var alvara = Assert.Single(lista.Itens, i => i.Tipo == TipoDocumentoEmpresa.Outro);
            Assert.Equal("Alvará de funcionamento", alvara.Titulo);
            Assert.Equal(2, alvara.TotalVersoes);

            var historico = await servico.HistoricoAsync(c.Empresa.Id, TipoDocumentoEmpresa.CrfFgts, null, CancellationToken.None);
            Assert.Equal(new[] { segunda.Id, primeira.Id }, historico.Select(h => h.Id));

            var baixado = await servico.BaixarAsync(primeira.Id, CancellationToken.None);
            Assert.Equal("crf antigo.pdf", baixado.NomeArquivo);
            Assert.Equal(Pdf, baixado.Conteudo);

            // Excluir a versão que vale: a anterior volta a valer; o arquivo some.
            await servico.ExcluirAsync(segunda.Id, CancellationToken.None);
            var depois = (await servico.ListarAsync(c.Empresa.Id, CancellationToken.None)).Itens.Single(i => i.Tipo == TipoDocumentoEmpresa.CrfFgts);
            Assert.Equal(primeira.Id, depois.Atual!.Id);
            Assert.False(File.Exists(Path.Combine(c.Pasta, c.Conta.Id.ToString("N"), c.Empresa.Id.ToString("N"), "documentos", $"{segunda.Id:N}.pdf")));

            // Painel: o FGTS antigo vence em 10 dias.
            var resumo = await servico.ResumoAsync(CancellationToken.None);
            Assert.Equal(0, resumo.Vencidos);
            Assert.Equal(1, resumo.VenceEmBreve);
            Assert.Equal("Regularidade do FGTS (CRF)", Assert.Single(resumo.Itens).Titulo);

            var db = sp.GetRequiredService<AppDbContext>();
            Assert.Equal(4, await db.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.TenantId == c.Conta.Id && a.Operacao == "EnviarDocumentoEmpresa"));
            Assert.True(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.TenantId == c.Conta.Id && a.Operacao == "ExcluirDocumentoEmpresa"));
        }
        finally
        {
            await LimparAsync(sp, c);
        }
    }

    [Fact]
    public async Task Envio_conferido_e_bloqueado_pelo_plano_sem_travar_o_que_ja_existe()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var c = await CriarAsync(sp);
        try
        {
            var servico = Servico(sp, c);
            var hoje = DataBrasilia.Hoje();

            var naoPdf = await Assert.ThrowsAsync<RegraNegocioException>(() => servico.EnviarAsync(
                Pedido(c.Empresa.Id, TipoDocumentoEmpresa.CartaoCnpj, hoje), new ArquivoDocumentoEmpresa("foto.pdf", Encoding.ASCII.GetBytes("GIF89a")), CancellationToken.None));
            Assert.Contains("PDF", naoPdf.Message);

            var cartao = await servico.EnviarAsync(Pedido(c.Empresa.Id, TipoDocumentoEmpresa.CartaoCnpj, hoje),
                new ArquivoDocumentoEmpresa("cartao.pdf", Pdf), CancellationToken.None);
            Assert.Equal(SituacaoDocumentoEmpresa.SemValidade, cartao.Situacao);

            // Operador bloqueia o recurso para a conta: não envia, mas lista e baixa.
            await sp.GetRequiredService<IAdministrarRecursosUseCase>().SalvarExcecaoAsync(c.Conta.Id,
                new SalvarExcecaoRecursoRequest(CatalogoRecursos.DocumentosEmpresa, false, null, "Teste de bloqueio do recurso", null), null, CancellationToken.None);

            var bloqueado = await Assert.ThrowsAsync<RegraNegocioException>(() => servico.EnviarAsync(
                Pedido(c.Empresa.Id, TipoDocumentoEmpresa.CartaoCnpj, hoje), new ArquivoDocumentoEmpresa("cartao2.pdf", Pdf), CancellationToken.None));
            Assert.Contains("não está incluído no seu plano", bloqueado.Message);

            var lista = await servico.ListarAsync(c.Empresa.Id, CancellationToken.None);
            Assert.False(lista.EnvioLiberado);
            Assert.NotNull(lista.MensagemBloqueio);
            Assert.Equal(cartao.Id, lista.Itens.Single(i => i.Tipo == TipoDocumentoEmpresa.CartaoCnpj).Atual!.Id);
            Assert.Equal(Pdf, (await servico.BaixarAsync(cartao.Id, CancellationToken.None)).Conteudo);

            // Empresa de outra conta não é achada.
            await Assert.ThrowsAsync<RecursoNaoEncontradoException>(() => servico.ListarAsync(Guid.NewGuid(), CancellationToken.None));
        }
        finally
        {
            await LimparAsync(sp, c);
        }
    }

    [Fact]
    public async Task Aviso_sai_uma_vez_por_faixa_e_recomeca_com_a_versao_nova()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var c = await CriarAsync(sp);
        try
        {
            var servico = Servico(sp, c);
            var aviso = sp.GetRequiredService<IAvisarDocumentosEmpresaVencendoUseCase>();
            var db = sp.GetRequiredService<AppDbContext>();
            var hoje = DataBrasilia.Hoje();

            var cndt = await servico.EnviarAsync(Pedido(c.Empresa.Id, TipoDocumentoEmpresa.Cndt, hoje.AddDays(-10), hoje.AddDays(5)),
                new ArquivoDocumentoEmpresa("cndt.pdf", Pdf), CancellationToken.None);
            // Sem validade: nunca avisa.
            await servico.EnviarAsync(Pedido(c.Empresa.Id, TipoDocumentoEmpresa.ContratoSocial, hoje),
                new ArquivoDocumentoEmpresa("contrato.pdf", Pdf), CancellationToken.None);

            var primeiro = await aviso.ExecutarAsync(c.Empresa.Id, hoje, CancellationToken.None);
            Assert.Equal(1, primeiro.Avisados);
            Assert.Contains(c.Empresa.Email, primeiro.Destinatarios);

            var chave = AvisarDocumentosEmpresaVencendoUseCase.Chave(cndt.Id, RegrasDocumentoEmpresa.FaixaVenceEmBreve);
            var email = await db.EmailLogs.IgnoreQueryFilters().SingleAsync(e => e.ChaveDeduplicacao == chave);
            Assert.Equal(TipoEmailCatalogo.DocumentoVencendo, email.Tipo);
            Assert.Contains("vence em 5 dias", email.Assunto);
            Assert.Contains("cndt-certidao.tst.jus.br", email.CorpoHtml);

            // Mesmo dia de novo: nada repetido.
            Assert.Equal(0, (await aviso.ExecutarAsync(c.Empresa.Id, hoje, CancellationToken.None)).Avisados);

            // Passou da validade: faixa "vencido" sai uma vez.
            var vencido = await aviso.ExecutarAsync(c.Empresa.Id, hoje.AddDays(6), CancellationToken.None);
            Assert.Equal(1, vencido.Avisados);
            Assert.True(await db.EmailLogs.IgnoreQueryFilters().AnyAsync(e =>
                e.ChaveDeduplicacao == AvisarDocumentosEmpresaVencendoUseCase.Chave(cndt.Id, RegrasDocumentoEmpresa.FaixaVencido)));

            // Versão nova, válida por 180 dias: a antiga sai da conta e nada é avisado.
            await servico.EnviarAsync(Pedido(c.Empresa.Id, TipoDocumentoEmpresa.Cndt, hoje),
                new ArquivoDocumentoEmpresa("cndt nova.pdf", Pdf), CancellationToken.None);
            Assert.Equal(0, (await aviso.ExecutarAsync(c.Empresa.Id, hoje.AddDays(6), CancellationToken.None)).Avisados);
        }
        finally
        {
            await LimparAsync(sp, c);
        }
    }
}
