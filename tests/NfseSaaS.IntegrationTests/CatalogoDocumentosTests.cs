using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.DocumentosEmpresa;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// DOC4: catálogo no banco, escolha por empresa (padrão, salvar, copiar),
/// envio só do que a empresa gerencia, link próprio, sugestão vinda de OUTRA
/// conta do mesmo município e link oficial do operador vencendo a sugestão.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class CatalogoDocumentosTests
{
    private const string LauroDeFreitas = "2919207";
    private readonly AplicacaoTesteFactory _factory;

    public CatalogoDocumentosTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private static async Task<(Tenant Conta, List<Empresa> Empresas)> CriarContaAsync(IServiceProvider sp, string nome, params string[] cnpjs)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = nome + " " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        var empresas = cnpjs.Select(cnpj => new Empresa
        {
            Cnpj = cnpj, RazaoSocial = $"{nome} {cnpj}", InscricaoMunicipal = "1", CodigoMunicipio = LauroDeFreitas, Uf = "BA",
            Cep = "42700000", Logradouro = "Rua", Numero = "1", Bairro = "Centro", Telefone = "71999999999", Email = "doc4@teste.com",
            OpSimpNac = "3", RegApTribSN = "1", RegEspTrib = "0", TribIssqn = "1", CstPisCofins = "00", PercentualTotalTributosSimplesNacional = "6.00"
        }).ToList();
        db.Empresas.AddRange(empresas);
        await db.SaveChangesAsync();
        return (conta, empresas);
    }

    private static async Task LimparContaAsync(IServiceProvider sp, Guid tenantId)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        db.ChangeTracker.Clear();
        await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.DocumentosGerenciadosEmpresa.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
        await db.Tenants.Where(x => x.Id == tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Escolha_link_proprio_sugestao_entre_contas_e_link_oficial()
    {
        using var escopoA = _factory.Services.CreateScope();
        using var escopoB = _factory.Services.CreateScope();
        var a = escopoA.ServiceProvider;
        var b = escopoB.ServiceProvider;
        var (contaA, empresasA) = await CriarContaAsync(a, "Conta A", "04747304000178", "11222333000181");
        var (contaB, empresasB) = await CriarContaAsync(b, "Conta B", "04747304000178");
        var a1 = empresasA[0].Id;
        var a2 = empresasA[1].Id;
        var b1 = empresasB[0].Id;
        int? tipoNovo = null;
        Guid? pacoteNovo = null;

        try
        {
            var dbA = a.GetRequiredService<AppDbContext>();
            var pgr = await dbA.TiposDocumento.Where(t => t.Codigo == "pgr").Select(t => t.Id).SingleAsync();
            Assert.True(pgr >= TiposDocumentoSistema.PrimeiroIdLivre); // seeder rodou e o Id não colide com os fixos

            var escolhaA = a.GetRequiredService<IEscolhaDocumentosEmpresaUseCase>();
            var documentosA = a.GetRequiredService<IDocumentosEmpresaUseCase>();

            // Ainda não escolheu: os 8 padrão, e os pacotes por ramo disponíveis.
            var inicial = await escolhaA.ObterAsync(a1, CancellationToken.None);
            Assert.False(inicial.JaEscolheu);
            Assert.Equal(8, inicial.Categorias.SelectMany(c => c.Tipos).Count(t => t.Marcado));
            Assert.Contains(inicial.Pacotes, p => p.Nome.Contains("indústria") && p.Tipos.Contains(pgr));
            Assert.Equal(1, inicial.OutrasEmpresas);

            // Escolhe 3: só eles aparecem; enviar outro tipo é barrado.
            await escolhaA.SalvarAsync(new SalvarEscolhaDocumentosRequest(a1, new[] { TiposDocumentoSistema.CndFederal, TiposDocumentoSistema.CndMunicipal, pgr }), CancellationToken.None);
            var lista = await documentosA.ListarAsync(a1, CancellationToken.None);
            Assert.True(lista.JaEscolheu);
            Assert.Equal(new[] { TiposDocumentoSistema.CndFederal, TiposDocumentoSistema.CndMunicipal, pgr }.OrderBy(x => x),
                lista.Itens.Select(i => i.Tipo).OrderBy(x => x));
            Assert.Contains(lista.Tipos, t => t.Tipo == TiposDocumentoSistema.Outro); // "Outro" sempre pode
            var barrado = await Assert.ThrowsAsync<RegraNegocioException>(() => documentosA.EnviarAsync(
                new EnviarDocumentoEmpresaRequest(a1, TiposDocumentoSistema.Cndt, null, DateOnly.FromDateTime(DateTime.Today), null, null),
                new ArquivoDocumentoEmpresa("cndt.pdf", "%PDF-1.4"u8.ToArray()), CancellationToken.None));
            Assert.Contains("Escolher documentos", barrado.Message);

            // Federal: link do catálogo. Municipal: ainda sem link.
            Assert.Equal(OrigemLinkDocumento.Catalogo, lista.Itens.Single(i => i.Tipo == TiposDocumentoSistema.CndFederal).Link!.Origem);
            Assert.Null(lista.Itens.Single(i => i.Tipo == TiposDocumentoSistema.CndMunicipal).Link);

            // Copia para a outra empresa da conta.
            Assert.Equal(1, await escolhaA.CopiarParaOutrasAsync(new CopiarEscolhaDocumentosRequest(a1), CancellationToken.None));
            Assert.Equal(3, (await documentosA.ListarAsync(a2, CancellationToken.None)).Itens.Count);

            // Conta B (mesmo município, ainda na lista padrão) cadastra o link da prefeitura (.gov.br).
            var proprio = await b.GetRequiredService<IEscolhaDocumentosEmpresaUseCase>().SalvarLinkAsync(
                new SalvarLinkDocumentoRequest(b1, TiposDocumentoSistema.CndMunicipal, "www.laurodefreitas.ba.gov.br/cnd"), CancellationToken.None);
            Assert.Equal(OrigemLinkDocumento.Empresa, proprio!.Origem);
            Assert.False((await b.GetRequiredService<IEscolhaDocumentosEmpresaUseCase>().ObterAsync(b1, CancellationToken.None)).JaEscolheu);

            // Conta A passa a ver a sugestão (só o endereço).
            dbA.ChangeTracker.Clear();
            var municipalA = (await documentosA.ListarAsync(a1, CancellationToken.None)).Itens.Single(i => i.Tipo == TiposDocumentoSistema.CndMunicipal);
            Assert.Equal(OrigemLinkDocumento.Sugerido, municipalA.Link!.Origem);
            Assert.Equal("https://www.laurodefreitas.ba.gov.br/cnd", municipalA.Link.Link);
            Assert.Contains("Lauro de Freitas", municipalA.Link.Texto);

            // Link fora de .gov.br de uma conta só não é espalhado.
            await b.GetRequiredService<IEscolhaDocumentosEmpresaUseCase>().SalvarLinkAsync(
                new SalvarLinkDocumentoRequest(b1, TiposDocumentoSistema.CndMunicipal, "https://site-qualquer.com.br/cnd"), CancellationToken.None);
            dbA.ChangeTracker.Clear();
            Assert.Null((await documentosA.ListarAsync(a1, CancellationToken.None)).Itens.Single(i => i.Tipo == TiposDocumentoSistema.CndMunicipal).Link);

            // Operador oficializa o link do município: vale para todos, antes de qualquer sugestão.
            var admin = a.GetRequiredService<IAdministrarCatalogoDocumentosUseCase>();
            var usados = await admin.LinksAsync(TiposDocumentoSistema.CndMunicipal, CancellationToken.None);
            var usoB = Assert.Single(usados.UsadosPelasEmpresas, u => u.Link == "https://site-qualquer.com.br/cnd");
            Assert.False(usoB.Sugerido);
            await admin.SalvarLinkLocalAsync(TiposDocumentoSistema.CndMunicipal,
                new SalvarLinkLocalDocumentoRequest(null, LauroDeFreitas, "https://www.laurodefreitas.ba.gov.br/certidoes"), CancellationToken.None);
            dbA.ChangeTracker.Clear();
            var oficial = (await documentosA.ListarAsync(a1, CancellationToken.None)).Itens.Single(i => i.Tipo == TiposDocumentoSistema.CndMunicipal).Link!;
            Assert.Equal(OrigemLinkDocumento.Municipio, oficial.Origem);
            Assert.Equal("https://www.laurodefreitas.ba.gov.br/certidoes", oficial.Link);

            // O link próprio da empresa vence o oficial; remover volta ao oficial.
            Assert.Equal(OrigemLinkDocumento.Empresa, (await escolhaA.SalvarLinkAsync(
                new SalvarLinkDocumentoRequest(a1, TiposDocumentoSistema.CndMunicipal, "https://minha.prefeitura.com.br"), CancellationToken.None))!.Origem);
            Assert.Equal(OrigemLinkDocumento.Municipio, (await escolhaA.SalvarLinkAsync(
                new SalvarLinkDocumentoRequest(a1, TiposDocumentoSistema.CndMunicipal, null), CancellationToken.None))!.Origem);

            // Operador cria um tipo e um pacote com ele; a empresa vê no catálogo.
            tipoNovo = await admin.SalvarTipoAsync(null, new SalvarTipoDocumentoRequest(
                "Laudo de teste " + Guid.NewGuid().ToString("N")[..6], CategoriaDocumento.Tecnico, AbrangenciaDocumento.Nacional,
                null, null, 365, false, true, false, 50), CancellationToken.None);
            Assert.True(tipoNovo >= TiposDocumentoSistema.PrimeiroIdLivre);
            pacoteNovo = await admin.SalvarPacoteAsync(null, new SalvarPacoteDocumentoRequest("Pacote de teste", null, new[] { tipoNovo.Value, pgr }, true, 999), CancellationToken.None);
            var comNovo = await escolhaA.ObterAsync(a1, CancellationToken.None);
            Assert.Contains(comNovo.Categorias.SelectMany(c => c.Tipos), t => t.Tipo == tipoNovo && !t.Marcado);
            Assert.Contains(comNovo.Pacotes, p => p.Id == pacoteNovo);

            // Inativado: some para quem não usa.
            await admin.SalvarTipoAsync(tipoNovo, new SalvarTipoDocumentoRequest(
                "Laudo inativo " + Guid.NewGuid().ToString("N")[..6], CategoriaDocumento.Tecnico, AbrangenciaDocumento.Nacional,
                null, null, 365, false, false, false, 50), CancellationToken.None);
            Assert.DoesNotContain((await escolhaA.ObterAsync(a1, CancellationToken.None)).Categorias.SelectMany(c => c.Tipos), t => t.Tipo == tipoNovo);
        }
        finally
        {
            var db = a.GetRequiredService<AppDbContext>();
            db.ChangeTracker.Clear();
            await db.LinksLocaisDocumento.Where(l => l.CodigoMunicipio == LauroDeFreitas && l.TipoDocumentoId == TiposDocumentoSistema.CndMunicipal).ExecuteDeleteAsync();
            if (pacoteNovo is { } p)
                await db.PacotesDocumento.Where(x => x.Id == p).ExecuteDeleteAsync();
            await LimparContaAsync(a, contaA.Id);
            await LimparContaAsync(b, contaB.Id);
            if (tipoNovo is { } t)
                await db.TiposDocumento.Where(x => x.Id == t).ExecuteDeleteAsync();
        }
    }
}
