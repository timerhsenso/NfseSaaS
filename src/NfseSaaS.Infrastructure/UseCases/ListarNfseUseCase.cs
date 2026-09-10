using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ListarNfseUseCase : IListarNfseUseCase
{
    private readonly AppDbContext _db;

    public ListarNfseUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<NfseResponse>> ExecutarAsync(ListarNfseRequest request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paginacao.Normalizar(request.Page, request.PageSize);

        var query = _db.NotasFiscais.AsNoTracking().AsQueryable();

        if (request.EmpresaId is not null)
            query = query.Where(n => n.EmpresaId == request.EmpresaId.Value);

        if (request.Status is not null)
            query = query.Where(n => n.Status == request.Status.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var itens = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NfseResponse(
                n.Id, n.EmpresaId, n.ClienteId, n.ContratoId, n.NumeroDps, n.SerieDps, n.NumeroNfse, n.ChaveAcesso,
                n.DataCompetencia, n.DataEmissao, n.ValorServico, n.ValorLiquido, n.DescricaoServico, n.Status,
                n.CodigoErro, n.MensagemErro, n.CreatedAt, n.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<NfseResponse>
        {
            Items = itens,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}
