using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.DocumentosEmpresa;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>DOC4: ver IEscolhaDocumentosEmpresaUseCase.</summary>
public sealed class EscolhaDocumentosEmpresaService : IEscolhaDocumentosEmpresaUseCase
{
    private readonly AppDbContext _db;
    private readonly ICatalogoDocumentos _catalogo;
    private readonly IAuditLogWriter _auditLogWriter;

    public EscolhaDocumentosEmpresaService(AppDbContext db, ICatalogoDocumentos catalogo, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _catalogo = catalogo;
        _auditLogWriter = auditLogWriter;
    }

    private async Task GarantirEmpresaAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        if (!await _db.Empresas.AnyAsync(e => e.Id == empresaId, cancellationToken))
            throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");
    }

    public async Task<EscolhaDocumentosResponse> ObterAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        await GarantirEmpresaAsync(empresaId, cancellationToken);
        var catalogo = await _catalogo.ObterAsync(cancellationToken);
        var situacao = await GerenciadosEmpresa.ObterAsync(_db, empresaId, catalogo, cancellationToken);
        var marcados = situacao.Tipos.ToHashSet();

        var comArquivo = (await _db.DocumentosEmpresa.AsNoTracking()
            .Where(d => d.EmpresaId == empresaId)
            .Select(d => d.Tipo).Distinct().ToListAsync(cancellationToken)).ToHashSet();

        // Clientes que esta empresa enxerga (os dela e os compartilhados da conta).
        var exigencias = await _db.Clientes.AsNoTracking()
            .Where(c => (c.EmpresaId == empresaId || c.EmpresaId == null) && c.DocumentosExigidos != null)
            .Select(c => c.DocumentosExigidos)
            .ToListAsync(cancellationToken);
        var clientesPorTipo = exigencias
            .SelectMany(e => RegrasDocumentosExigidos.Ler(e).Select(c => c.Tipo).Distinct())
            .GroupBy(t => t)
            .ToDictionary(g => g.Key, g => g.Count());

        // Inativo só aparece para quem ainda usa.
        var visiveis = catalogo.Todos
            .Where(t => t.Tipo != TiposDocumentoSistema.Outro && (t.Ativo || marcados.Contains(t.Tipo)))
            .ToList();

        var categorias = GerenciadosEmpresa.Ordenar(visiveis)
            .GroupBy(t => t.Categoria)
            .Select(g => new CategoriaEscolhaDocumentos(g.Key, CatalogoDocumentosEmpresa.NomeCategoria(g.Key), g.Select(t =>
                new OpcaoEscolhaDocumento(t.Tipo, t.Nome, t.Orgao, t.Abrangencia, t.ValidadeSugeridaDias, marcados.Contains(t.Tipo),
                    t.PadraoNovasEmpresas, comArquivo.Contains(t.Tipo), clientesPorTipo.GetValueOrDefault(t.Tipo), !t.Ativo)).ToList()))
            .ToList();

        var pacotes = (await _db.PacotesDocumento.AsNoTracking().Where(p => p.Ativo).ToListAsync(cancellationToken))
            .OrderBy(p => p.Ordem).ThenBy(p => p.Nome, StringComparer.CurrentCulture)
            .Select(p => new PacoteDocumentoResponse(p.Id, p.Nome, p.Descricao,
                p.TiposDocumentoIds.Where(t => catalogo.Buscar(t) is { Ativo: true }).Distinct().ToList()))
            .Where(p => p.Tipos.Count > 0)
            .ToList();

        var outras = await _db.Empresas.CountAsync(e => e.Id != empresaId && e.Ativo, cancellationToken);
        return new EscolhaDocumentosResponse(empresaId, situacao.JaEscolheu, categorias, pacotes, outras);
    }

    public async Task SalvarAsync(SalvarEscolhaDocumentosRequest request, CancellationToken cancellationToken)
    {
        await GarantirEmpresaAsync(request.EmpresaId, cancellationToken);
        var catalogo = await _catalogo.ObterAsync(cancellationToken);
        var antes = await GerenciadosEmpresa.ObterAsync(_db, request.EmpresaId, catalogo, cancellationToken);
        var tipos = RegrasCatalogoDocumentos.ValidarEscolha(request.Tipos, catalogo, antes.Tipos.ToList());

        await AplicarAsync(request.EmpresaId, tipos, cancellationToken);
        _auditLogWriter.Registrar("EscolherDocumentosEmpresa", "Empresa", request.EmpresaId, new
        {
            Incluidos = tipos.Except(antes.Tipos).Select(t => catalogo.Titulo(t, null)).ToList(),
            Retirados = antes.Tipos.Except(tipos).Select(t => catalogo.Titulo(t, null)).ToList()
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Deixa a empresa exatamente com estes tipos e marca que ela escolheu. Quem fica mantém o link da própria empresa.</summary>
    private async Task AplicarAsync(Guid empresaId, IReadOnlyCollection<int> tipos, CancellationToken cancellationToken)
    {
        var linhas = await _db.DocumentosGerenciadosEmpresa.Where(d => d.EmpresaId == empresaId).ToListAsync(cancellationToken);
        foreach (var linha in linhas.Where(l => !tipos.Contains(l.TipoDocumentoId)))
            _db.DocumentosGerenciadosEmpresa.Remove(linha);
        foreach (var tipo in tipos.Where(t => linhas.All(l => l.TipoDocumentoId != t)))
            _db.DocumentosGerenciadosEmpresa.Add(new DocumentoGerenciadoEmpresa { EmpresaId = empresaId, TipoDocumentoId = tipo });

        var empresa = await _db.Empresas.FirstAsync(e => e.Id == empresaId, cancellationToken);
        empresa.DocumentosEscolhidosEm = DateTimeOffset.UtcNow;
        empresa.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public async Task<int> CopiarParaOutrasAsync(CopiarEscolhaDocumentosRequest request, CancellationToken cancellationToken)
    {
        await GarantirEmpresaAsync(request.EmpresaOrigemId, cancellationToken);
        var catalogo = await _catalogo.ObterAsync(cancellationToken);
        var origem = await GerenciadosEmpresa.ObterAsync(_db, request.EmpresaOrigemId, catalogo, cancellationToken);

        var destinos = await _db.Empresas.AsNoTracking()
            .Where(e => e.Id != request.EmpresaOrigemId && e.Ativo)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
        foreach (var destino in destinos)
            await AplicarAsync(destino, origem.Tipos, cancellationToken);

        _auditLogWriter.Registrar("CopiarDocumentosEmpresa", "Empresa", request.EmpresaOrigemId, new
        {
            Empresas = destinos.Count,
            Documentos = origem.Tipos.Select(t => catalogo.Titulo(t, null)).ToList()
        });
        await _db.SaveChangesAsync(cancellationToken);
        return destinos.Count;
    }

    public async Task<LinkDocumentoResolvido?> SalvarLinkAsync(SalvarLinkDocumentoRequest request, CancellationToken cancellationToken)
    {
        await GarantirEmpresaAsync(request.EmpresaId, cancellationToken);
        var catalogo = await _catalogo.ObterAsync(cancellationToken);
        var info = catalogo.Buscar(request.Tipo);
        if (info is null || info.Tipo == TiposDocumentoSistema.Outro)
            throw new RegraNegocioException("Escolha um documento da lista da empresa.");
        var situacao = await GerenciadosEmpresa.ObterAsync(_db, request.EmpresaId, catalogo, cancellationToken);
        if (!situacao.Tipos.Contains(info.Tipo))
            throw new RegraNegocioException($"\"{info.Nome}\" não está na lista desta empresa.");

        var link = RegrasLinkDocumento.Normalizar(request.Link);
        var linha = await _db.DocumentosGerenciadosEmpresa
            .FirstOrDefaultAsync(d => d.EmpresaId == request.EmpresaId && d.TipoDocumentoId == info.Tipo, cancellationToken);
        if (linha is null)
        {
            // Ainda na lista padrão: o link entra numa linha própria (e o tipo segue no padrão).
            linha = new DocumentoGerenciadoEmpresa { EmpresaId = request.EmpresaId, TipoDocumentoId = info.Tipo };
            _db.DocumentosGerenciadosEmpresa.Add(linha);
        }
        var antes = linha.LinkProprio;
        linha.LinkProprio = link;
        linha.UpdatedAt = DateTimeOffset.UtcNow;
        _auditLogWriter.Registrar("AlterarLinkDocumentoEmpresa", "Empresa", request.EmpresaId,
            new { Documento = info.Nome, Antes = antes, Depois = link });
        await _db.SaveChangesAsync(cancellationToken);

        var proprios = new Dictionary<int, string?> { [info.Tipo] = link };
        return (await LinksDocumentoEmpresa.ResolverAsync(_db, request.EmpresaId, new[] { info }, proprios, cancellationToken))
            .GetValueOrDefault(info.Tipo);
    }
}
