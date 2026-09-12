using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers.Api;

public sealed record GrupoResponse(Guid Id, string Nome, string? Descricao, bool Padrao, bool EhAdministrador, int QuantidadeUsuarios);

public sealed record TelaResponse(Guid Id, string Codigo, string Nome, int Ordem);

public sealed record PermissaoTelaResponse(Guid TelaId, string TelaCodigo, string TelaNome, bool Incluir, bool Alterar, bool Excluir, bool Consultar);

public sealed record GrupoDetalheResponse(Guid Id, string Nome, string? Descricao, bool Padrao, bool EhAdministrador, IReadOnlyList<PermissaoTelaResponse> Permissoes);

public sealed record CriarGrupoRequest(string Nome, string? Descricao);

public sealed record AtualizarGrupoRequest(string Nome, string? Descricao);

public sealed record PermissaoTelaRequest(Guid TelaId, bool Incluir, bool Alterar, bool Excluir, bool Consultar);

public sealed record AtualizarPermissoesRequest(IReadOnlyList<PermissaoTelaRequest> Permissoes);

/// <summary>
/// Módulo de segurança IAEC — CRUD de Grupo e edição da matriz de
/// permissão (Grupo × Tela). Ver Grupo/GrupoTela/GrupoProvisionamentoService.
/// </summary>
[ApiController]
[Route("api/grupos")]
[RequerPermissao(TelaCatalogo.Grupos, AcaoPermissao.Consultar)]
public sealed class GruposController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;

    public GruposController(AppDbContext db, ICurrentTenant currentTenant)
    {
        _db = db;
        _currentTenant = currentTenant;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken)
    {
        var grupos = await _db.Grupos.AsNoTracking()
            .OrderBy(g => g.Nome)
            .Select(g => new GrupoResponse(
                g.Id, g.Nome, g.Descricao, g.Padrao, g.EhAdministrador,
                _db.Users.Count(u => u.GrupoId == g.Id)))
            .ToListAsync(cancellationToken);

        return Ok(grupos);
    }

    /// <summary>Catálogo de Telas (colunas da matriz) — global, não filtrado por Tenant.</summary>
    [HttpGet("telas")]
    public async Task<IActionResult> ListarTelas(CancellationToken cancellationToken)
    {
        var telas = await _db.Telas.AsNoTracking()
            .OrderBy(t => t.Ordem)
            .Select(t => new TelaResponse(t.Id, t.Codigo, t.Nome, t.Ordem))
            .ToListAsync(cancellationToken);

        return Ok(telas);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> ObterPorId(Guid id, CancellationToken cancellationToken)
    {
        var grupo = await _db.Grupos.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (grupo is null)
            return NotFound();

        var permissoesExistentes = await _db.GrupoTelas.AsNoTracking()
            .Where(gt => gt.GrupoId == id)
            .ToDictionaryAsync(gt => gt.TelaId, cancellationToken);

        var telas = await _db.Telas.AsNoTracking().OrderBy(t => t.Ordem).ToListAsync(cancellationToken);

        // Toda Tela aparece na resposta, mesmo sem linha em GrupoTela —
        // ausência de linha == tudo false (ver comentário em GrupoTela) —
        // assim o front não precisa adivinhar quais colunas existem.
        var permissoes = telas.Select(t =>
        {
            permissoesExistentes.TryGetValue(t.Id, out var gt);
            return new PermissaoTelaResponse(t.Id, t.Codigo, t.Nome, gt?.Incluir ?? false, gt?.Alterar ?? false, gt?.Excluir ?? false, gt?.Consultar ?? false);
        }).ToList();

        return Ok(new GrupoDetalheResponse(grupo.Id, grupo.Nome, grupo.Descricao, grupo.Padrao, grupo.EhAdministrador, permissoes));
    }

    [RequerPermissao(TelaCatalogo.Grupos, AcaoPermissao.Incluir)]
    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarGrupoRequest request, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Nome))
            return BadRequest(new { erro = "Nome é obrigatório." });

        var jaExiste = await _db.Grupos.AnyAsync(g => g.Nome == request.Nome, cancellationToken);
        if (jaExiste)
            return Conflict(new { erro = "Já existe um grupo com este nome." });

        var grupo = new Grupo
        {
            TenantId = tenantId,
            Nome = request.Nome.Trim(),
            Descricao = string.IsNullOrWhiteSpace(request.Descricao) ? null : request.Descricao.Trim(),
            Padrao = false,
            EhAdministrador = false
        };

        _db.Grupos.Add(grupo);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { grupoId = grupo.Id });
    }

    [RequerPermissao(TelaCatalogo.Grupos, AcaoPermissao.Alterar)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] AtualizarGrupoRequest request, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is null)
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Nome))
            return BadRequest(new { erro = "Nome é obrigatório." });

        var grupo = await _db.Grupos.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (grupo is null)
            return NotFound();

        var jaExiste = await _db.Grupos.AnyAsync(g => g.Nome == request.Nome && g.Id != id, cancellationToken);
        if (jaExiste)
            return Conflict(new { erro = "Já existe um grupo com este nome." });

        grupo.Nome = request.Nome.Trim();
        grupo.Descricao = string.IsNullOrWhiteSpace(request.Descricao) ? null : request.Descricao.Trim();

        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Substitui a matriz de permissão inteira do Grupo. Bloqueado pro
    /// grupo Administrador padrão — ele sempre tem acesso total, editar
    /// a matriz dele é a forma mais fácil de travar o Tenant inteiro por
    /// engano.
    /// </summary>
    [RequerPermissao(TelaCatalogo.Grupos, AcaoPermissao.Alterar)]
    [HttpPut("{id:guid}/permissoes")]
    public async Task<IActionResult> AtualizarPermissoes(Guid id, [FromBody] AtualizarPermissoesRequest request, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            return Unauthorized();

        var grupo = await _db.Grupos.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (grupo is null)
            return NotFound();

        if (grupo.EhAdministrador)
            return Conflict(new { erro = "O grupo Administrador sempre tem acesso total — a matriz dele não pode ser editada." });

        var telaIdsValidos = (await _db.Telas.AsNoTracking().Select(t => t.Id).ToListAsync(cancellationToken)).ToHashSet();

        var linhasAtuais = await _db.GrupoTelas.Where(gt => gt.GrupoId == id).ToDictionaryAsync(gt => gt.TelaId, cancellationToken);

        foreach (var permissao in request.Permissoes)
        {
            if (!telaIdsValidos.Contains(permissao.TelaId))
                continue; // TelaId desconhecido — ignora silenciosamente em vez de falhar a requisição inteira por um item ruim

            if (linhasAtuais.TryGetValue(permissao.TelaId, out var linha))
            {
                linha.Incluir = permissao.Incluir;
                linha.Alterar = permissao.Alterar;
                linha.Excluir = permissao.Excluir;
                linha.Consultar = permissao.Consultar;
            }
            else if (permissao.Incluir || permissao.Alterar || permissao.Excluir || permissao.Consultar)
            {
                // Só cria a linha se ALGUMA permissão for true — tudo
                // false não precisa de linha (ver comentário em GrupoTela).
                _db.GrupoTelas.Add(new GrupoTela
                {
                    TenantId = tenantId,
                    GrupoId = id,
                    TelaId = permissao.TelaId,
                    Incluir = permissao.Incluir,
                    Alterar = permissao.Alterar,
                    Excluir = permissao.Excluir,
                    Consultar = permissao.Consultar
                });
            }
        }

        // Linhas existentes que vieram TODAS false no request — remove, não deixa lixo zerado na tabela.
        foreach (var telaId in linhasAtuais.Keys)
        {
            var permissaoEnviada = request.Permissoes.FirstOrDefault(p => p.TelaId == telaId);
            if (permissaoEnviada is not null && !permissaoEnviada.Incluir && !permissaoEnviada.Alterar && !permissaoEnviada.Excluir && !permissaoEnviada.Consultar)
                _db.GrupoTelas.Remove(linhasAtuais[telaId]);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Exclusão REAL — bloqueada pro grupo Administrador padrão e pra grupo com usuário vinculado.</summary>
    [RequerPermissao(TelaCatalogo.Grupos, AcaoPermissao.Excluir)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken cancellationToken)
    {
        var grupo = await _db.Grupos.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (grupo is null)
            return NotFound();

        if (grupo.EhAdministrador)
            return Conflict(new { erro = "O grupo Administrador padrão não pode ser excluído." });

        var totalUsuarios = await _db.Users.CountAsync(u => u.GrupoId == id, cancellationToken);
        if (totalUsuarios > 0)
            return Conflict(new { erro = $"Não é possível excluir: {totalUsuarios} usuário(s) vinculado(s) a este grupo. Troque o grupo deles antes." });

        _db.Grupos.Remove(grupo);
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}
