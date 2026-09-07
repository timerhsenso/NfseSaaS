using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Servicos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ListarServicosUseCase : IListarServicosUseCase
{
    private readonly AppDbContext _db;

    public ListarServicosUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<ServicoResponse>> ExecutarAsync(ListarServicosRequest request, CancellationToken cancellationToken)
    {
        var empresaExiste = await _db.Empresas.AnyAsync(e => e.Id == request.EmpresaId, cancellationToken);
        if (!empresaExiste)
            throw new RecursoNaoEncontradoException($"Empresa {request.EmpresaId} não encontrada.");

        var (page, pageSize) = Paginacao.Normalizar(request.Page, request.PageSize);

        var query = _db.Servicos.AsNoTracking().Where(s => s.EmpresaId == request.EmpresaId);

        if (!request.IncluirInativos)
            query = query.Where(s => s.Ativo);

        if (!string.IsNullOrWhiteSpace(request.Busca))
        {
            var busca = request.Busca.Trim();
            query = query.Where(s => EF.Functions.ILike(s.Descricao, $"%{busca}%") || EF.Functions.ILike(s.CodigoTributacaoNacional, $"%{busca}%"));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var itens = await query
            .OrderBy(s => s.Descricao)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new ServicoResponse(
                s.Id, s.EmpresaId, s.Descricao, s.CodigoTributacaoNacional, s.CodigoNbs, s.ValorPadrao,
                s.Ativo, s.CreatedAt, s.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<ServicoResponse>
        {
            Items = itens,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}
