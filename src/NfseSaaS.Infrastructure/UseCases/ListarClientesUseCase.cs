using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ListarClientesUseCase : IListarClientesUseCase
{
    private readonly AppDbContext _db;

    public ListarClientesUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<ClienteResponse>> ExecutarAsync(ListarClientesRequest request, CancellationToken cancellationToken)
    {
        var empresaExiste = await _db.Empresas.AnyAsync(e => e.Id == request.EmpresaId, cancellationToken);
        if (!empresaExiste)
            throw new RecursoNaoEncontradoException($"Empresa {request.EmpresaId} não encontrada.");

        var (page, pageSize) = Paginacao.Normalizar(request.Page, request.PageSize);

        var query = _db.Clientes.AsNoTracking().Where(c => c.EmpresaId == request.EmpresaId);

        if (!request.IncluirInativos)
            query = query.Where(c => c.Ativo);

        if (!string.IsNullOrWhiteSpace(request.Busca))
        {
            var busca = request.Busca.Trim();
            query = query.Where(c => EF.Functions.ILike(c.Nome, $"%{busca}%") || EF.Functions.ILike(c.CpfCnpj, $"%{busca}%"));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var itens = await query
            .OrderBy(c => c.Nome)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new ClienteResponse(
                c.Id, c.EmpresaId, c.CpfCnpj, c.Nome, c.Email, c.Telefone, c.CodigoMunicipio,
                c.Cep, c.Logradouro, c.Numero, c.Bairro, c.Ativo, c.CreatedAt, c.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<ClienteResponse>
        {
            Items = itens,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}
