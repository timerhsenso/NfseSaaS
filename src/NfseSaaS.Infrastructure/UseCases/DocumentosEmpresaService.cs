using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.DocumentosEmpresa;
using NfseSaaS.Application.UseCases.Recursos;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Documents;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// DOC1: ver IDocumentosEmpresaUseCase. A versão que vale é a mais recente
/// de cada "lugar" (Empresa + Tipo + NomeChave). O volume por Empresa é
/// pequeno (dezenas de linhas), então o agrupamento é feito em memória.
/// </summary>
public sealed class DocumentosEmpresaService : IDocumentosEmpresaUseCase
{
    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly IRecursosConta _recursos;
    private readonly DocumentoEmpresaFileStore _store;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly ICatalogoDocumentos _catalogo;

    public DocumentosEmpresaService(
        AppDbContext db,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        IRecursosConta recursos,
        DocumentoEmpresaFileStore store,
        IAuditLogWriter auditLogWriter,
        ICatalogoDocumentos catalogo)
    {
        _db = db;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _recursos = recursos;
        _store = store;
        _auditLogWriter = auditLogWriter;
        _catalogo = catalogo;
    }

    private Guid TenantId => _currentTenant.TenantId
        ?? throw new InvalidOperationException("Tenant não definido para os documentos da empresa.");

    private async Task GarantirEmpresaAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        if (!await _db.Empresas.AnyAsync(e => e.Id == empresaId, cancellationToken))
            throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");
    }

    public async Task<ListaDocumentosEmpresaResponse> ListarAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        await GarantirEmpresaAsync(empresaId, cancellationToken);
        var recurso = await _recursos.ObterAsync(TenantId, CatalogoRecursos.DocumentosEmpresa, cancellationToken);
        var catalogo = await _catalogo.ObterAsync(cancellationToken);
        var gerenciados = await GerenciadosEmpresa.ObterAsync(_db, empresaId, catalogo, cancellationToken);

        var documentos = await _db.DocumentosEmpresa.AsNoTracking()
            .Where(d => d.EmpresaId == empresaId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);
        var nomes = await NomesUsuariosAsync(documentos, cancellationToken);
        var hoje = DataBrasilia.Hoje();

        var grupos = documentos
            .GroupBy(d => (d.Tipo, d.NomeChave))
            .ToDictionary(g => g.Key, g => g.ToList());

        // DOC4: só os documentos que a empresa gerencia (desmarcar esconde, não apaga).
        var tipos = GerenciadosEmpresa.Ordenar(gerenciados.Tipos.Select(catalogo.Obter)).ToList();
        var links = await LinksDocumentoEmpresa.ResolverAsync(_db, empresaId, tipos, gerenciados.LinksProprios, cancellationToken);

        var itens = new List<ItemDocumentoEmpresaResponse>();
        foreach (var info in tipos)
        {
            grupos.TryGetValue((info.Tipo, string.Empty), out var versoes);
            itens.Add(Item(info, null, versoes, nomes, hoje, links.GetValueOrDefault(info.Tipo), gerenciados.LinksProprios.GetValueOrDefault(info.Tipo)));
        }
        var outro = catalogo.Obter(TiposDocumentoSistema.Outro);
        foreach (var grupo in grupos.Where(g => g.Key.Tipo == TiposDocumentoSistema.Outro)
                     .OrderBy(g => g.Value[0].NomePersonalizado, StringComparer.CurrentCultureIgnoreCase))
            itens.Add(Item(outro, grupo.Value[0].NomePersonalizado, grupo.Value, nomes, hoje, null, null));

        return new ListaDocumentosEmpresaResponse(
            empresaId,
            recurso.Ligado,
            recurso.Ligado ? null : RegrasRecursos.MensagemBloqueado(recurso),
            tipos.Append(outro).ToList(),
            itens,
            gerenciados.JaEscolheu);
    }

    private static ItemDocumentoEmpresaResponse Item(
        TipoDocumentoInfo info, string? nomePersonalizado, List<DocumentoEmpresa>? versoes, IReadOnlyDictionary<Guid, string> nomes, DateOnly hoje,
        LinkDocumentoResolvido? link, string? linkProprio)
    {
        var atual = versoes is { Count: > 0 } ? Versao(versoes[0], nomes, hoje) : null;
        var titulo = info.Tipo == TiposDocumentoSistema.Outro && !string.IsNullOrWhiteSpace(nomePersonalizado) ? nomePersonalizado.Trim() : info.Nome;
        return new ItemDocumentoEmpresaResponse(info.Tipo, titulo, nomePersonalizado, info.Orgao, info.Categoria, info.CategoriaNome,
            info.Abrangencia, link, linkProprio, atual, versoes?.Count ?? 0);
    }

    private static VersaoDocumentoEmpresaResponse Versao(DocumentoEmpresa d, IReadOnlyDictionary<Guid, string> nomes, DateOnly hoje)
    {
        var situacao = RegrasDocumentoEmpresa.Situacao(d.ValidaAte, hoje);
        var dias = RegrasDocumentoEmpresa.DiasParaVencer(d.ValidaAte, hoje);
        return new VersaoDocumentoEmpresaResponse(d.Id, d.NomeArquivo, d.TamanhoBytes, d.DataEmissao, d.ValidaAte,
            situacao, RegrasDocumentoEmpresa.TextoSituacao(situacao, dias), dias, d.Observacao, d.CreatedAt,
            d.EnviadoPorUsuarioId is { } u ? nomes.GetValueOrDefault(u) : null);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> NomesUsuariosAsync(IEnumerable<DocumentoEmpresa> documentos, CancellationToken cancellationToken)
    {
        var ids = documentos.Where(d => d.EnviadoPorUsuarioId.HasValue).Select(d => d.EnviadoPorUsuarioId!.Value).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();
        return await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.UserName ?? string.Empty, cancellationToken);
    }

    public async Task<IReadOnlyList<VersaoDocumentoEmpresaResponse>> HistoricoAsync(
        Guid empresaId, int tipo, string? nomePersonalizado, CancellationToken cancellationToken)
    {
        await GarantirEmpresaAsync(empresaId, cancellationToken);
        (await _catalogo.ObterAsync(cancellationToken)).Obter(tipo);
        var chave = RegrasDocumentoEmpresa.NomeChave(tipo, nomePersonalizado);

        var versoes = await _db.DocumentosEmpresa.AsNoTracking()
            .Where(d => d.EmpresaId == empresaId && d.Tipo == tipo && d.NomeChave == chave)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);
        var nomes = await NomesUsuariosAsync(versoes, cancellationToken);
        var hoje = DataBrasilia.Hoje();
        return versoes.Select(v => Versao(v, nomes, hoje)).ToList();
    }

    public async Task<VersaoDocumentoEmpresaResponse> EnviarAsync(
        EnviarDocumentoEmpresaRequest request, ArquivoDocumentoEmpresa arquivo, CancellationToken cancellationToken)
    {
        await GarantirEmpresaAsync(request.EmpresaId, cancellationToken);

        var recurso = await _recursos.ObterAsync(TenantId, CatalogoRecursos.DocumentosEmpresa, cancellationToken);
        if (!recurso.Ligado)
            throw new RegraNegocioException(RegrasRecursos.MensagemBloqueado(recurso));

        var catalogo = await _catalogo.ObterAsync(cancellationToken);
        var info = catalogo.Buscar(request.Tipo)
            ?? throw new RegraNegocioException("Escolha o tipo do documento.");
        // DOC4: só os documentos que a empresa gerencia (e o "Outro", que é livre).
        if (request.Tipo != TiposDocumentoSistema.Outro
            && !(await GerenciadosEmpresa.ObterAsync(_db, request.EmpresaId, catalogo, cancellationToken)).Tipos.Contains(request.Tipo))
            throw new RegraNegocioException($"\"{info.Nome}\" não está na lista desta empresa: marque em \"Escolher documentos\" antes de enviar.");

        RegrasDocumentoEmpresa.ValidarArquivo(arquivo.NomeArquivo, arquivo.Conteudo);
        var hoje = DataBrasilia.Hoje();
        var dados = RegrasDocumentoEmpresa.Validar(info, request.NomePersonalizado, request.DataEmissao, request.ValidaAte, request.Observacao, hoje);

        // "Outro" com o mesmo nome de um já existente: usa a grafia que já está lá
        // (o histórico continua com um título só).
        var nomePersonalizado = dados.NomePersonalizado;
        if (request.Tipo == TiposDocumentoSistema.Outro)
        {
            nomePersonalizado = await _db.DocumentosEmpresa.AsNoTracking()
                .Where(d => d.EmpresaId == request.EmpresaId && d.Tipo == TiposDocumentoSistema.Outro && d.NomeChave == dados.NomeChave)
                .OrderBy(d => d.CreatedAt)
                .Select(d => d.NomePersonalizado)
                .FirstOrDefaultAsync(cancellationToken) ?? nomePersonalizado;
        }

        var documento = new DocumentoEmpresa
        {
            EmpresaId = request.EmpresaId,
            Tipo = request.Tipo,
            NomePersonalizado = nomePersonalizado,
            NomeChave = dados.NomeChave,
            DataEmissao = dados.DataEmissao,
            ValidaAte = dados.ValidaAte,
            NomeArquivo = RegrasDocumentoEmpresa.NomeArquivoSeguro(arquivo.NomeArquivo),
            TamanhoBytes = arquivo.Conteudo.LongLength,
            Observacao = dados.Observacao,
            EnviadoPorUsuarioId = _currentUser.UserId
        };

        // Disco antes do banco: se gravar o arquivo falhar, nada aponta para um arquivo que não existe.
        await _store.SalvarAsync(TenantId, request.EmpresaId, documento.Id, arquivo.Conteudo, cancellationToken);

        _db.DocumentosEmpresa.Add(documento);
        _auditLogWriter.Registrar("EnviarDocumentoEmpresa", "Empresa", request.EmpresaId, new
        {
            DocumentoId = documento.Id,
            Documento = catalogo.Titulo(documento.Tipo, documento.NomePersonalizado),
            documento.DataEmissao,
            documento.ValidaAte,
            documento.NomeArquivo,
            documento.TamanhoBytes
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            _store.Excluir(TenantId, request.EmpresaId, documento.Id);
            throw;
        }

        var nomes = await NomesUsuariosAsync(new[] { documento }, cancellationToken);
        return Versao(documento, nomes, hoje);
    }

    public async Task<ArquivoDocumentoEmpresa> BaixarAsync(Guid documentoId, CancellationToken cancellationToken)
    {
        var documento = await _db.DocumentosEmpresa.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Documento {documentoId} não encontrado.");

        try
        {
            var conteudo = await _store.LerAsync(documento.TenantId, documento.EmpresaId, documento.Id, cancellationToken);
            return new ArquivoDocumentoEmpresa(documento.NomeArquivo, conteudo);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new RegraNegocioException("O arquivo deste documento não foi encontrado no servidor. Envie o documento de novo.");
        }
    }

    public async Task ExcluirAsync(Guid documentoId, CancellationToken cancellationToken)
    {
        var documento = await _db.DocumentosEmpresa.FirstOrDefaultAsync(d => d.Id == documentoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Documento {documentoId} não encontrado.");

        var catalogo = await _catalogo.ObterAsync(cancellationToken);
        _db.DocumentosEmpresa.Remove(documento);
        _auditLogWriter.Registrar("ExcluirDocumentoEmpresa", "Empresa", documento.EmpresaId, new
        {
            DocumentoId = documento.Id,
            Documento = catalogo.Titulo(documento.Tipo, documento.NomePersonalizado),
            documento.ValidaAte,
            documento.NomeArquivo
        });
        await _db.SaveChangesAsync(cancellationToken);

        // Depois do banco: se apagar o arquivo falhar, sobra só um arquivo solto (sem nada apontando).
        try
        {
            _store.Excluir(documento.TenantId, documento.EmpresaId, documento.Id);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public async Task<ResumoDocumentosEmpresaResponse> ResumoAsync(CancellationToken cancellationToken)
    {
        var hoje = DataBrasilia.Hoje();
        var catalogo = await _catalogo.ObterAsync(cancellationToken);
        var atuais = await AtuaisAsync(_db, null, cancellationToken);
        var empresas = await _db.Empresas.AsNoTracking().Where(e => e.Ativo)
            .ToDictionaryAsync(e => e.Id, e => e.RazaoSocial, cancellationToken);

        var itens = atuais
            .Where(d => empresas.ContainsKey(d.EmpresaId) && d.ValidaAte.HasValue)
            .Select(d => (Doc: d, Situacao: RegrasDocumentoEmpresa.Situacao(d.ValidaAte, hoje)))
            .Where(x => x.Situacao is SituacaoDocumentoEmpresa.Vencido or SituacaoDocumentoEmpresa.VenceEmBreve)
            .OrderBy(x => x.Doc.ValidaAte)
            .Select(x => new AlertaDocumentoEmpresa(x.Doc.EmpresaId, empresas[x.Doc.EmpresaId],
                catalogo.Titulo(x.Doc.Tipo, x.Doc.NomePersonalizado), x.Doc.ValidaAte!.Value, x.Situacao,
                RegrasDocumentoEmpresa.TextoSituacao(x.Situacao, RegrasDocumentoEmpresa.DiasParaVencer(x.Doc.ValidaAte, hoje))))
            .ToList();

        return new ResumoDocumentosEmpresaResponse(
            itens.Count(i => i.Situacao == SituacaoDocumentoEmpresa.Vencido),
            itens.Count(i => i.Situacao == SituacaoDocumentoEmpresa.VenceEmBreve),
            itens.Take(20).ToList());
    }

    /// <summary>DOC2: versões que valem por Empresa, no formato do envio do e-mail (conta atual).</summary>
    internal static async Task<Dictionary<Guid, List<DocumentoAtual>>> AtuaisParaEmailAsync(
        AppDbContext db, IReadOnlyCollection<Guid> empresaIds, CatalogoDocumentosEmpresa catalogo, CancellationToken cancellationToken)
    {
        if (empresaIds.Count == 0)
            return new Dictionary<Guid, List<DocumentoAtual>>();

        var todos = await db.DocumentosEmpresa.AsNoTracking()
            .Where(d => empresaIds.Contains(d.EmpresaId))
            .ToListAsync(cancellationToken);
        return todos
            .GroupBy(d => (d.EmpresaId, d.Tipo, d.NomeChave))
            .Select(g => g.OrderByDescending(d => d.CreatedAt).First())
            .GroupBy(d => d.EmpresaId)
            .ToDictionary(g => g.Key, g => g.Select(d => new DocumentoAtual(d.Id, d.Tipo, d.NomeChave,
                catalogo.Titulo(d.Tipo, d.NomePersonalizado), d.NomeArquivo, d.TamanhoBytes, d.ValidaAte)).ToList());
    }

    /// <summary>A versão que vale de cada "lugar" (da conta atual; de uma Empresa só, se informada).</summary>
    internal static async Task<List<DocumentoEmpresa>> AtuaisAsync(AppDbContext db, Guid? empresaId, CancellationToken cancellationToken)
    {
        var consulta = db.DocumentosEmpresa.AsNoTracking();
        if (empresaId is { } id)
            consulta = consulta.Where(d => d.EmpresaId == id);

        var todos = await consulta.ToListAsync(cancellationToken);
        return todos
            .GroupBy(d => (d.EmpresaId, d.Tipo, d.NomeChave))
            .Select(g => g.OrderByDescending(d => d.CreatedAt).First())
            .ToList();
    }
}
