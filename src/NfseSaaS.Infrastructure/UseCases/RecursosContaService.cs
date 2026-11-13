using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Recursos;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// PL1: resolve os recursos da conta (exceção → plano → padrão) e administra
/// a grade por plano e as exceções por conta (Plataforma). Sem cache: são
/// três consultas pequenas por chamada, e a mudança do operador vale na hora.
/// </summary>
public sealed class RecursosContaService : IRecursosConta, IAdministrarRecursosUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public RecursosContaService(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    // ---------- Resolução ----------

    public async Task<RecursoResolvido> ObterAsync(Guid tenantId, string recurso, CancellationToken cancellationToken) =>
        (await ResolverAsync(tenantId, CatalogoRecursos.ObterOuErro(recurso), cancellationToken)).Single();

    public async Task<bool> LigadoAsync(Guid tenantId, string recurso, CancellationToken cancellationToken) =>
        (await ObterAsync(tenantId, recurso, cancellationToken)).Ligado;

    public async Task GarantirCabeMaisUmAsync(Guid tenantId, string recurso, int atual, CancellationToken cancellationToken)
    {
        var resolvido = await ObterAsync(tenantId, recurso, cancellationToken);
        if (!RegrasRecursos.CabeMaisUm(resolvido.Limite, atual))
            throw new RegraNegocioException(RegrasRecursos.MensagemLimite(resolvido, atual));
    }

    public Task<IReadOnlyList<RecursoResolvido>> ListarAsync(Guid tenantId, CancellationToken cancellationToken) =>
        ResolverAsync(tenantId, null, cancellationToken);

    /// <summary>Um recurso (ou todos, com null) já resolvidos para a conta.</summary>
    private async Task<IReadOnlyList<RecursoResolvido>> ResolverAsync(Guid tenantId, RecursoPlano? so, CancellationToken cancellationToken)
    {
        var perfil = await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => (PerfilConta?)t.Perfil)
            .FirstOrDefaultAsync(cancellationToken) ?? PerfilConta.Empresa;
        var planoId = await PlanoDaContaAsync(tenantId, cancellationToken);
        var recursos = so is null ? CatalogoRecursos.Todos : new[] { so };
        var codigos = recursos.Select(r => r.Codigo).ToList();

        var doPlano = planoId is { } id
            ? await _db.PlanosRecursos.AsNoTracking().Where(p => p.PlanoId == id && codigos.Contains(p.Recurso))
                .ToDictionaryAsync(p => p.Recurso, cancellationToken)
            : new Dictionary<string, PlanoRecurso>();

        var hoje = DataBrasilia.Hoje();
        var excecoes = (await _db.ExcecoesRecursoConta.AsNoTracking()
                .Where(e => e.TenantId == tenantId && codigos.Contains(e.Recurso))
                .ToListAsync(cancellationToken))
            .Where(e => RegrasRecursos.ExcecaoVale(e.ValidaAte, hoje))
            .ToDictionary(e => e.Recurso);

        return recursos.Select(r =>
        {
            var plano = doPlano.GetValueOrDefault(r.Codigo);
            var excecao = excecoes.GetValueOrDefault(r.Codigo);
            return RegrasRecursos.Resolver(r, perfil,
                plano is null ? null : new ValorRecurso(plano.Ligado, plano.Limite),
                excecao is null ? null : new ValorRecurso(excecao.Ligado, excecao.Limite),
                excecao?.ValidaAte);
        }).ToList();
    }

    /// <summary>Plano da assinatura (pelo código); nulo = sem plano (teste, beta ou cobrança desligada).</summary>
    private async Task<Guid?> PlanoDaContaAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var codigo = await _db.Assinaturas.AsNoTracking().Where(a => a.TenantId == tenantId).Select(a => a.Plano)
            .FirstOrDefaultAsync(cancellationToken);
        if (codigo is null)
            return null;
        return await _db.Planos.AsNoTracking().Where(p => p.Codigo == codigo).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
    }

    // ---------- Plataforma › Planos › Recursos ----------

    public async Task<RecursosPlanoResponse> RecursosDoPlanoAsync(Guid planoId, CancellationToken cancellationToken)
    {
        var plano = await _db.Planos.AsNoTracking().Where(p => p.Id == planoId).Select(p => new { p.Id, p.Nome })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new RecursoNaoEncontradoException("Plano não encontrado.");
        var linhas = await _db.PlanosRecursos.AsNoTracking().Where(p => p.PlanoId == planoId)
            .ToDictionaryAsync(p => p.Recurso, cancellationToken);

        return new RecursosPlanoResponse(plano.Id, plano.Nome, CatalogoRecursos.Todos.Select(r =>
        {
            var linha = linhas.GetValueOrDefault(r.Codigo);
            return new ItemRecursoPlano(r.Codigo, r.Nome, r.Descricao, r.Tipo, linha is not null, linha?.Ligado, linha?.Limite, r.TextoPadrao);
        }).ToList());
    }

    public async Task<RecursosPlanoResponse> SalvarRecursosDoPlanoAsync(Guid planoId, SalvarRecursosPlanoRequest request, Guid? operadorId, CancellationToken cancellationToken)
    {
        if (!await _db.Planos.AnyAsync(p => p.Id == planoId, cancellationToken))
            throw new RecursoNaoEncontradoException("Plano não encontrado.");

        var linhas = await _db.PlanosRecursos.Where(p => p.PlanoId == planoId).ToListAsync(cancellationToken);
        var mudancas = new List<object>();
        foreach (var item in request.Itens ?? Array.Empty<ValorRecursoPlanoRequest>())
        {
            var recurso = CatalogoRecursos.ObterOuErro(item.Codigo);
            var linha = linhas.FirstOrDefault(l => l.Recurso == recurso.Codigo);

            if (item.Padrao)
            {
                if (linha is null) continue;
                mudancas.Add(new { recurso.Codigo, Antes = new { linha.Ligado, linha.Limite }, Depois = "padrão" });
                _db.PlanosRecursos.Remove(linha);
                continue;
            }

            var valor = RegrasRecursos.Normalizar(recurso, item.Ligado, item.Limite);
            if (linha is null)
            {
                _db.PlanosRecursos.Add(new PlanoRecurso { PlanoId = planoId, Recurso = recurso.Codigo, Ligado = valor.Ligado, Limite = valor.Limite });
                mudancas.Add(new { recurso.Codigo, Antes = "padrão", Depois = valor });
            }
            else if (linha.Ligado != valor.Ligado || linha.Limite != valor.Limite)
            {
                mudancas.Add(new { recurso.Codigo, Antes = new { linha.Ligado, linha.Limite }, Depois = valor });
                linha.Ligado = valor.Ligado;
                linha.Limite = valor.Limite;
                linha.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        if (mudancas.Count > 0)
        {
            _auditLogWriter.Registrar("AlterarRecursosPlano", "Plano", planoId, new { Mudancas = mudancas, OperadorId = operadorId });
            await _db.SaveChangesAsync(cancellationToken);
        }

        return await RecursosDoPlanoAsync(planoId, cancellationToken);
    }

    // ---------- Plataforma › Contas › Recursos (exceções) ----------

    public async Task<RecursosContaResponse> RecursosDaContaAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!await _db.Tenants.AnyAsync(t => t.Id == tenantId, cancellationToken))
            throw new RecursoNaoEncontradoException($"Conta {tenantId} não encontrada.");

        var planoId = await PlanoDaContaAsync(tenantId, cancellationToken);
        var planoNome = planoId is { } id
            ? await _db.Planos.AsNoTracking().Where(p => p.Id == id).Select(p => p.Nome).FirstOrDefaultAsync(cancellationToken)
            : null;

        var excecoes = await _db.ExcecoesRecursoConta.AsNoTracking().Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.Recurso).ToListAsync(cancellationToken);
        var autores = excecoes.Where(e => e.CriadoPorUsuarioId is not null).Select(e => e.CriadoPorUsuarioId!.Value).Distinct().ToList();
        var emails = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => autores.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);

        var hoje = DataBrasilia.Hoje();
        var itens = excecoes.Select(e =>
        {
            var recurso = CatalogoRecursos.Obter(e.Recurso);
            return new ExcecaoRecursoResponse(e.Id, e.Recurso, recurso?.Nome ?? e.Recurso, recurso?.Tipo ?? TipoRecurso.LigaDesliga,
                e.Ligado, e.Limite, e.Motivo, e.ValidaAte, !RegrasRecursos.ExcecaoVale(e.ValidaAte, hoje),
                e.UpdatedAt ?? e.CreatedAt, e.CriadoPorUsuarioId is { } u ? emails.GetValueOrDefault(u) : null);
        }).ToList();

        return new RecursosContaResponse(tenantId, planoNome, await ListarAsync(tenantId, cancellationToken), itens);
    }

    public async Task<RecursosContaResponse> SalvarExcecaoAsync(Guid tenantId, SalvarExcecaoRecursoRequest request, Guid? operadorId, CancellationToken cancellationToken)
    {
        if (!await _db.Tenants.AnyAsync(t => t.Id == tenantId, cancellationToken))
            throw new RecursoNaoEncontradoException($"Conta {tenantId} não encontrada.");

        var recurso = CatalogoRecursos.ObterOuErro(request.Recurso);
        var valor = RegrasRecursos.Normalizar(recurso, request.Ligado, request.Limite);
        var motivo = RegrasRecursos.ValidarMotivo(request.Motivo);
        if (request.ValidaAte is { } ate && ate < DataBrasilia.Hoje())
            throw new RegraNegocioException("A data da exceção já passou. Deixe em branco para valer sem prazo.");

        var excecao = await _db.ExcecoesRecursoConta.FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Recurso == recurso.Codigo, cancellationToken);
        object? antes = null;
        if (excecao is null)
        {
            excecao = new ExcecaoRecursoConta { TenantId = tenantId, Recurso = recurso.Codigo };
            _db.ExcecoesRecursoConta.Add(excecao);
        }
        else
        {
            antes = new { excecao.Ligado, excecao.Limite, excecao.Motivo, excecao.ValidaAte };
            excecao.UpdatedAt = DateTimeOffset.UtcNow;
        }

        excecao.Ligado = valor.Ligado;
        excecao.Limite = valor.Limite;
        excecao.Motivo = motivo;
        excecao.ValidaAte = request.ValidaAte;
        excecao.CriadoPorUsuarioId = operadorId;

        await SalvarComHistoricoAsync(tenantId, "SalvarExcecaoRecurso",
            new { recurso.Codigo, Antes = antes, Depois = new { valor.Ligado, valor.Limite, Motivo = motivo, request.ValidaAte }, OperadorId = operadorId },
            cancellationToken);
        return await RecursosDaContaAsync(tenantId, cancellationToken);
    }

    public async Task<RecursosContaResponse> RemoverExcecaoAsync(Guid tenantId, Guid excecaoId, Guid? operadorId, CancellationToken cancellationToken)
    {
        var excecao = await _db.ExcecoesRecursoConta.FirstOrDefaultAsync(e => e.Id == excecaoId && e.TenantId == tenantId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException("Exceção não encontrada.");

        _db.ExcecoesRecursoConta.Remove(excecao);
        await SalvarComHistoricoAsync(tenantId, "RemoverExcecaoRecurso",
            new { Codigo = excecao.Recurso, Antes = new { excecao.Ligado, excecao.Limite, excecao.Motivo, excecao.ValidaAte }, OperadorId = operadorId },
            cancellationToken);
        return await RecursosDaContaAsync(tenantId, cancellationToken);
    }

    /// <summary>Histórico na conta afetada (mesmo padrão de isentar/prorrogar teste).</summary>
    private async Task SalvarComHistoricoAsync(Guid tenantId, string acao, object detalhes, CancellationToken cancellationToken)
    {
        _db.TenantIdOverrideDeSistema = tenantId;
        try
        {
            _auditLogWriter.Registrar(acao, "ExcecaoRecursoConta", tenantId, detalhes);
            await _db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _db.TenantIdOverrideDeSistema = null;
        }
    }
}
