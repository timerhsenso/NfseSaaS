using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.UseCases.DocumentosEmpresa;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>DOC4: ver ICatalogoDocumentos. Sem cache: são poucas dezenas de linhas e a mudança do operador vale na hora.</summary>
public sealed class CatalogoDocumentosService : ICatalogoDocumentos
{
    private readonly AppDbContext _db;

    public CatalogoDocumentosService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<CatalogoDocumentosEmpresa> ObterAsync(CancellationToken cancellationToken) =>
        new((await _db.TiposDocumento.AsNoTracking().ToListAsync(cancellationToken)).Select(Info));

    public static TipoDocumentoInfo Info(TipoDocumento t) => new(
        t.Id, t.Codigo, t.Nome, t.Categoria, t.Abrangencia, string.IsNullOrWhiteSpace(t.Orgao) ? "—" : t.Orgao,
        t.ExigeValidade, t.ValidadeDias, t.LinkEmissao, t.Ativo, t.PadraoNovasEmpresas, t.Ordem);
}

/// <summary>
/// DOC4: resolve o link oficial de cada documento de UMA empresa (ver
/// RegrasLinkDocumento). As sugestões olham empresas de TODAS as contas do
/// mesmo município/UF, mas só o endereço do site (nunca arquivo, nome ou conta).
/// </summary>
internal static class LinksDocumentoEmpresa
{
    internal sealed record Local(string? Uf, string? CodigoMunicipio, string? NomeMunicipio);

    internal static async Task<Local> LocalDaEmpresaAsync(AppDbContext db, Guid empresaId, CancellationToken cancellationToken)
    {
        var empresa = await db.Empresas.AsNoTracking().IgnoreQueryFilters()
            .Where(e => e.Id == empresaId)
            .Select(e => new { e.Uf, e.CodigoMunicipio })
            .FirstOrDefaultAsync(cancellationToken);
        var uf = string.IsNullOrWhiteSpace(empresa?.Uf) ? null : empresa!.Uf.Trim().ToUpperInvariant();
        var municipio = string.IsNullOrWhiteSpace(empresa?.CodigoMunicipio) ? null : empresa!.CodigoMunicipio.Trim();
        var nome = municipio is null
            ? null
            : await db.Municipios.AsNoTracking().Where(m => m.Codigo == municipio).Select(m => m.Nome).FirstOrDefaultAsync(cancellationToken);
        return new Local(uf, municipio, nome);
    }

    internal static async Task<Dictionary<int, LinkDocumentoResolvido?>> ResolverAsync(
        AppDbContext db, Guid empresaId, IReadOnlyCollection<TipoDocumentoInfo> tipos, IReadOnlyDictionary<int, string?> proprios,
        CancellationToken cancellationToken)
    {
        var resultado = new Dictionary<int, LinkDocumentoResolvido?>();
        if (tipos.Count == 0)
            return resultado;

        var local = await LocalDaEmpresaAsync(db, empresaId, cancellationToken);
        var ids = tipos.Select(t => t.Tipo).ToList();

        var oficiais = await db.LinksLocaisDocumento.AsNoTracking()
            .Where(l => ids.Contains(l.TipoDocumentoId)
                && ((l.CodigoMunicipio != null && l.CodigoMunicipio == local.CodigoMunicipio)
                    || (l.CodigoMunicipio == null && l.Uf != null && l.Uf == local.Uf)))
            .ToListAsync(cancellationToken);

        // Sugestão: só para os locais (estadual/municipal) sem link próprio nem oficial.
        var precisamSugestao = tipos
            .Where(t => t.Abrangencia != Domain.Enums.AbrangenciaDocumento.Nacional
                && string.IsNullOrWhiteSpace(proprios.GetValueOrDefault(t.Tipo))
                && !oficiais.Any(o => o.TipoDocumentoId == t.Tipo && (t.Abrangencia == Domain.Enums.AbrangenciaDocumento.Municipal ? o.CodigoMunicipio != null : o.CodigoMunicipio == null)))
            .ToList();
        var sugestoes = await SugestoesAsync(db, empresaId, local, precisamSugestao, cancellationToken);

        foreach (var info in tipos)
        {
            var municipio = oficiais.FirstOrDefault(o => o.TipoDocumentoId == info.Tipo && o.CodigoMunicipio != null)?.Link;
            var estado = oficiais.FirstOrDefault(o => o.TipoDocumentoId == info.Tipo && o.CodigoMunicipio == null)?.Link;
            var nomeLocal = info.Abrangencia == Domain.Enums.AbrangenciaDocumento.Municipal ? local.NomeMunicipio : local.Uf;
            resultado[info.Tipo] = RegrasLinkDocumento.Resolver(info, proprios.GetValueOrDefault(info.Tipo), municipio, estado,
                sugestoes.GetValueOrDefault(info.Tipo), nomeLocal);
        }
        return resultado;
    }

    private static async Task<Dictionary<int, string?>> SugestoesAsync(
        AppDbContext db, Guid empresaId, Local local, IReadOnlyCollection<TipoDocumentoInfo> tipos, CancellationToken cancellationToken)
    {
        var resultado = new Dictionary<int, string?>();
        var municipais = tipos.Where(t => t.Abrangencia == Domain.Enums.AbrangenciaDocumento.Municipal).Select(t => t.Tipo).ToList();
        var estaduais = tipos.Where(t => t.Abrangencia == Domain.Enums.AbrangenciaDocumento.Estadual).Select(t => t.Tipo).ToList();
        if ((municipais.Count == 0 || local.CodigoMunicipio is null) && (estaduais.Count == 0 || local.Uf is null))
            return resultado;

        var usos = await (
            from d in db.DocumentosGerenciadosEmpresa.AsNoTracking().IgnoreQueryFilters()
            join e in db.Empresas.AsNoTracking().IgnoreQueryFilters() on d.EmpresaId equals e.Id
            where d.LinkProprio != null && d.EmpresaId != empresaId
                && ((municipais.Contains(d.TipoDocumentoId) && e.CodigoMunicipio == local.CodigoMunicipio)
                    || (estaduais.Contains(d.TipoDocumentoId) && e.Uf == local.Uf))
            select new { d.TipoDocumentoId, d.LinkProprio, d.TenantId })
            .ToListAsync(cancellationToken);

        foreach (var grupo in usos.GroupBy(u => u.TipoDocumentoId))
        {
            resultado[grupo.Key] = RegrasLinkDocumento.MelhorSugestao(grupo
                .GroupBy(u => u.LinkProprio!)
                .Select(g => new UsoLinkDocumento(g.Key, g.Select(u => u.TenantId).Distinct().Count())));
        }
        return resultado;
    }
}

/// <summary>DOC4: quais tipos a empresa gerencia (a escolha salva ou o padrão) e os links próprios.</summary>
internal static class GerenciadosEmpresa
{
    internal sealed record Situacao(bool JaEscolheu, IReadOnlyList<int> Tipos, IReadOnlyDictionary<int, string?> LinksProprios);

    internal static async Task<Situacao> ObterAsync(AppDbContext db, Guid empresaId, CatalogoDocumentosEmpresa catalogo, CancellationToken cancellationToken)
    {
        var escolhidoEm = await db.Empresas.AsNoTracking()
            .Where(e => e.Id == empresaId)
            .Select(e => e.DocumentosEscolhidosEm)
            .FirstOrDefaultAsync(cancellationToken);
        var linhas = await db.DocumentosGerenciadosEmpresa.AsNoTracking()
            .Where(d => d.EmpresaId == empresaId)
            .Select(d => new { d.TipoDocumentoId, d.LinkProprio })
            .ToListAsync(cancellationToken);
        var links = linhas.ToDictionary(l => l.TipoDocumentoId, l => l.LinkProprio);

        if (escolhidoEm is not null)
            return new Situacao(true, linhas.Select(l => l.TipoDocumentoId).Where(t => catalogo.Buscar(t) is not null).ToList(), links);

        var comArquivo = await db.DocumentosEmpresa.AsNoTracking()
            .Where(d => d.EmpresaId == empresaId)
            .Select(d => d.Tipo)
            .Distinct()
            .ToListAsync(cancellationToken);
        var padrao = RegrasCatalogoDocumentos.PadraoDaEmpresa(catalogo, comArquivo)
            .Concat(linhas.Select(l => l.TipoDocumentoId)) // link salvo antes de escolher a lista
            .Distinct()
            .Where(t => catalogo.Buscar(t) is not null)
            .ToList();
        return new Situacao(false, padrao, links);
    }

    /// <summary>Ordem da tela: categoria (na ordem do catálogo), depois a ordem e o nome.</summary>
    internal static IEnumerable<TipoDocumentoInfo> Ordenar(IEnumerable<TipoDocumentoInfo> tipos)
    {
        var ordemCategoria = CatalogoDocumentosEmpresa.Categorias.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);
        return tipos.OrderBy(t => ordemCategoria.GetValueOrDefault(t.Categoria, 99))
            .ThenBy(t => t.Ordem)
            .ThenBy(t => t.Nome, StringComparer.CurrentCulture);
    }
}
