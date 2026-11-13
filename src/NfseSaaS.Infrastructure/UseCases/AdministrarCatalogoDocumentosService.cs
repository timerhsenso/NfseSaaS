using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.DocumentosEmpresa;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// DOC4: Plataforma › Documentos (ver IAdministrarCatalogoDocumentosUseCase).
/// As contagens e os links usados pelas empresas cruzam contas
/// (IgnoreQueryFilters), mas só números e endereços de site — nunca arquivo
/// nem nome de empresa.
/// </summary>
public sealed class AdministrarCatalogoDocumentosService : IAdministrarCatalogoDocumentosUseCase
{
    private static readonly string[] Ufs =
    {
        "AC", "AL", "AM", "AP", "BA", "CE", "DF", "ES", "GO", "MA", "MG", "MS", "MT", "PA", "PB",
        "PE", "PI", "PR", "RJ", "RN", "RO", "RR", "RS", "SC", "SE", "SP", "TO"
    };

    private readonly AppDbContext _db;
    private readonly ICatalogoDocumentos _catalogo;
    private readonly IAuditLogWriter _auditLogWriter;

    public AdministrarCatalogoDocumentosService(AppDbContext db, ICatalogoDocumentos catalogo, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _catalogo = catalogo;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<CatalogoDocumentosAdminResponse> ListarAsync(CancellationToken cancellationToken)
    {
        var tipos = await _db.TiposDocumento.AsNoTracking().ToListAsync(cancellationToken);
        var usando = await _db.DocumentosGerenciadosEmpresa.AsNoTracking().IgnoreQueryFilters()
            .GroupBy(d => d.TipoDocumentoId)
            .Select(g => new { Tipo = g.Key, Empresas = g.Select(d => d.EmpresaId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Tipo, x => x.Empresas, cancellationToken);
        var comArquivo = await _db.DocumentosEmpresa.AsNoTracking().IgnoreQueryFilters()
            .GroupBy(d => d.Tipo)
            .Select(g => new { Tipo = g.Key, Empresas = g.Select(d => d.EmpresaId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Tipo, x => x.Empresas, cancellationToken);
        var links = await _db.LinksLocaisDocumento.AsNoTracking()
            .GroupBy(l => l.TipoDocumentoId)
            .Select(g => new { Tipo = g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.Tipo, x => x.Total, cancellationToken);

        var catalogo = new CatalogoDocumentosEmpresa(tipos.Select(CatalogoDocumentosService.Info));
        var lista = GerenciadosEmpresa.Ordenar(catalogo.Todos).Select(info =>
        {
            var t = tipos.First(x => x.Id == info.Tipo);
            return new TipoDocumentoAdminResponse(t.Id, t.Codigo, t.Nome, t.Categoria, CatalogoDocumentosEmpresa.NomeCategoria(t.Categoria),
                t.Abrangencia, t.Orgao, t.LinkEmissao, t.ValidadeDias, t.ExigeValidade, t.Ativo, t.PadraoNovasEmpresas, t.Ordem,
                t.Id < TiposDocumentoSistema.PrimeiroIdLivre,
                Math.Max(usando.GetValueOrDefault(t.Id), comArquivo.GetValueOrDefault(t.Id)),
                links.GetValueOrDefault(t.Id));
        }).ToList();

        var pacotes = (await _db.PacotesDocumento.AsNoTracking().ToListAsync(cancellationToken))
            .OrderBy(p => p.Ordem).ThenBy(p => p.Nome, StringComparer.CurrentCulture)
            .Select(p => new PacoteDocumentoAdminResponse(p.Id, p.Codigo, p.Nome, p.Descricao, p.TiposDocumentoIds, p.Ativo, p.Ordem))
            .ToList();

        var categorias = CatalogoDocumentosEmpresa.Categorias
            .Select(c => new OpcaoCategoriaResponse(c, CatalogoDocumentosEmpresa.NomeCategoria(c)))
            .ToList();

        return new CatalogoDocumentosAdminResponse(lista, pacotes, categorias);
    }

    public async Task<int> SalvarTipoAsync(int? tipo, SalvarTipoDocumentoRequest request, CancellationToken cancellationToken)
    {
        var dados = RegrasCatalogoDocumentos.ValidarTipo(tipo, request);

        TipoDocumento entidade;
        object? antes = null;
        if (tipo is { } id)
        {
            entidade = await _db.TiposDocumento.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                ?? throw new RecursoNaoEncontradoException($"Tipo de documento {id} não encontrado.");
            antes = new { entidade.Nome, entidade.Categoria, entidade.Abrangencia, entidade.Orgao, entidade.LinkEmissao, entidade.ValidadeDias,
                entidade.ExigeValidade, entidade.Ativo, entidade.PadraoNovasEmpresas, entidade.Ordem };
            entidade.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            entidade = new TipoDocumento();
            _db.TiposDocumento.Add(entidade);
        }

        var nomeRepetido = await _db.TiposDocumento.AnyAsync(
            t => t.Id != entidade.Id && t.Nome.ToLower() == dados.Nome.ToLower(), cancellationToken);
        if (nomeRepetido)
            throw new RegraNegocioException($"Já existe um documento chamado \"{dados.Nome}\" no catálogo.");

        entidade.Nome = dados.Nome;
        entidade.Categoria = request.Categoria;
        entidade.Abrangencia = request.Abrangencia;
        entidade.Orgao = dados.Orgao;
        entidade.LinkEmissao = dados.LinkEmissao;
        entidade.ValidadeDias = dados.ValidadeDias;
        entidade.ExigeValidade = request.ExigeValidade;
        entidade.Ativo = request.Ativo;
        entidade.PadraoNovasEmpresas = request.PadraoNovasEmpresas;
        entidade.Ordem = request.Ordem;

        await _db.SaveChangesAsync(cancellationToken);
        _auditLogWriter.Registrar(tipo is null ? "CriarTipoDocumento" : "AlterarTipoDocumento", "TipoDocumento", null, new
        {
            Tipo = entidade.Id,
            Antes = antes,
            Depois = new { entidade.Nome, entidade.Categoria, entidade.Abrangencia, entidade.Orgao, entidade.LinkEmissao, entidade.ValidadeDias,
                entidade.ExigeValidade, entidade.Ativo, entidade.PadraoNovasEmpresas, entidade.Ordem }
        });
        await _db.SaveChangesAsync(cancellationToken);
        return entidade.Id;
    }

    public async Task<Guid> SalvarPacoteAsync(Guid? pacoteId, SalvarPacoteDocumentoRequest request, CancellationToken cancellationToken)
    {
        var catalogo = await _catalogo.ObterAsync(cancellationToken);
        var dados = RegrasCatalogoDocumentos.ValidarPacote(request, catalogo);

        PacoteDocumento pacote;
        if (pacoteId is { } id)
        {
            pacote = await _db.PacotesDocumento.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                ?? throw new RecursoNaoEncontradoException($"Pacote {id} não encontrado.");
            pacote.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            pacote = new PacoteDocumento();
            _db.PacotesDocumento.Add(pacote);
        }

        pacote.Nome = dados.Nome;
        pacote.Descricao = dados.Descricao;
        pacote.TiposDocumentoIds = dados.Tipos;
        pacote.Ativo = request.Ativo;
        pacote.Ordem = request.Ordem;

        _auditLogWriter.Registrar(pacoteId is null ? "CriarPacoteDocumentos" : "AlterarPacoteDocumentos", "PacoteDocumento", pacote.Id, new
        {
            pacote.Nome,
            pacote.Ativo,
            Documentos = dados.Tipos.Select(t => catalogo.Titulo(t, null)).ToList()
        });
        await _db.SaveChangesAsync(cancellationToken);
        return pacote.Id;
    }

    public async Task<LinksTipoDocumentoResponse> LinksAsync(int tipo, CancellationToken cancellationToken)
    {
        var info = await _db.TiposDocumento.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tipo, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Tipo de documento {tipo} não encontrado.");

        var oficiais = await _db.LinksLocaisDocumento.AsNoTracking()
            .Where(l => l.TipoDocumentoId == tipo)
            .ToListAsync(cancellationToken);

        // Links que as empresas cadastraram, por lugar (município ou UF, conforme o documento).
        var usos = await (
            from d in _db.DocumentosGerenciadosEmpresa.AsNoTracking().IgnoreQueryFilters()
            join e in _db.Empresas.AsNoTracking().IgnoreQueryFilters() on d.EmpresaId equals e.Id
            where d.TipoDocumentoId == tipo && d.LinkProprio != null
            select new { d.LinkProprio, d.TenantId, e.Uf, e.CodigoMunicipio })
            .ToListAsync(cancellationToken);

        var municipal = info.Abrangencia == AbrangenciaDocumento.Municipal;
        var codigos = oficiais.Select(o => o.CodigoMunicipio).Concat(usos.Select(u => u.CodigoMunicipio))
            .Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        var municipios = codigos.Count == 0
            ? new Dictionary<string, string>()
            : await _db.Municipios.AsNoTracking().Where(m => codigos.Contains(m.Codigo))
                .ToDictionaryAsync(m => m.Codigo, m => $"{m.Nome}/{m.Uf}", cancellationToken);

        string NomeLocal(string? uf, string? municipio) =>
            municipio is not null ? municipios.GetValueOrDefault(municipio) ?? municipio : uf ?? "—";

        var listaOficiais = oficiais
            .Select(o => new LinkLocalDocumentoResponse(o.Id, o.Uf, o.CodigoMunicipio, NomeLocal(o.Uf, o.CodigoMunicipio), o.Link))
            .OrderBy(o => o.Local, StringComparer.CurrentCulture)
            .ToList();

        var sugestoes = info.Abrangencia == AbrangenciaDocumento.Nacional
            ? new List<SugestaoLinkDocumentoResponse>()
            : usos
                .Select(u => new { Uf = municipal ? null : u.Uf, Municipio = municipal ? u.CodigoMunicipio : null, Link = u.LinkProprio!, u.TenantId })
                .Where(u => municipal ? !string.IsNullOrWhiteSpace(u.Municipio) : !string.IsNullOrWhiteSpace(u.Uf))
                .GroupBy(u => (u.Uf, u.Municipio))
                .SelectMany(lugar =>
                {
                    var porLink = lugar.GroupBy(u => u.Link)
                        .Select(g => new UsoLinkDocumento(g.Key, g.Select(x => x.TenantId).Distinct().Count()))
                        .ToList();
                    var melhor = RegrasLinkDocumento.MelhorSugestao(porLink);
                    return porLink.Select(p => new SugestaoLinkDocumentoResponse(lugar.Key.Uf, lugar.Key.Municipio,
                        NomeLocal(lugar.Key.Uf, lugar.Key.Municipio), p.Link, p.Contas,
                        oficiais.Any(o => o.Uf == lugar.Key.Uf && o.CodigoMunicipio == lugar.Key.Municipio && o.Link == p.Link),
                        p.Link == melhor));
                })
                .OrderBy(s => s.Local, StringComparer.CurrentCulture)
                .ThenByDescending(s => s.Contas)
                .ToList();

        return new LinksTipoDocumentoResponse(info.Id, info.Nome, info.Abrangencia, listaOficiais, sugestoes);
    }

    public async Task SalvarLinkLocalAsync(int tipo, SalvarLinkLocalDocumentoRequest request, CancellationToken cancellationToken)
    {
        var info = await _db.TiposDocumento.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tipo, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Tipo de documento {tipo} não encontrado.");

        string? uf = null;
        string? municipio = null;
        switch (info.Abrangencia)
        {
            case AbrangenciaDocumento.Estadual:
                uf = (request.Uf ?? string.Empty).Trim().ToUpperInvariant();
                if (!Ufs.Contains(uf))
                    throw new RegraNegocioException("Escolha o estado.");
                break;
            case AbrangenciaDocumento.Municipal:
                municipio = (request.CodigoMunicipio ?? string.Empty).Trim();
                var existe = await _db.Municipios.AsNoTracking().Where(m => m.Codigo == municipio).Select(m => m.Uf).FirstOrDefaultAsync(cancellationToken);
                if (existe is null)
                    throw new RegraNegocioException("Escolha o município.");
                break;
            default:
                throw new RegraNegocioException("Documento nacional usa o link geral do catálogo (edite o tipo).");
        }

        var link = RegrasLinkDocumento.Normalizar(request.Link);
        var atual = await _db.LinksLocaisDocumento
            .FirstOrDefaultAsync(l => l.TipoDocumentoId == tipo && l.Uf == uf && l.CodigoMunicipio == municipio, cancellationToken);
        var antes = atual?.Link;

        if (link is null)
        {
            if (atual is not null)
                _db.LinksLocaisDocumento.Remove(atual);
        }
        else if (atual is null)
        {
            _db.LinksLocaisDocumento.Add(new LinkLocalDocumento { TipoDocumentoId = tipo, Uf = uf, CodigoMunicipio = municipio, Link = link });
        }
        else
        {
            atual.Link = link;
            atual.UpdatedAt = DateTimeOffset.UtcNow;
        }

        _auditLogWriter.Registrar("AlterarLinkLocalDocumento", "TipoDocumento", null,
            new { Tipo = tipo, Documento = info.Nome, Uf = uf, Municipio = municipio, Antes = antes, Depois = link });
        await _db.SaveChangesAsync(cancellationToken);
    }
}
