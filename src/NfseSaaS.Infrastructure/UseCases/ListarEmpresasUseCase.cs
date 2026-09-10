using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.UseCases.Empresas;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ListarEmpresasUseCase : IListarEmpresasUseCase
{
    private readonly AppDbContext _db;

    public ListarEmpresasUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<EmpresaResponse>> ExecutarAsync(ListarEmpresasRequest request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paginacao.Normalizar(request.Page, request.PageSize);

        var query = _db.Empresas.AsNoTracking().AsQueryable();

        if (!request.IncluirInativas)
            query = query.Where(e => e.Ativo);

        if (!string.IsNullOrWhiteSpace(request.Busca))
        {
            var busca = request.Busca.Trim();
            query = query.Where(e => EF.Functions.ILike(e.RazaoSocial, $"%{busca}%") || EF.Functions.ILike(e.Cnpj, $"%{busca}%"));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var itens = await query
            .OrderBy(e => e.RazaoSocial)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new EmpresaResponse(
                e.Id, e.Cnpj, e.RazaoSocial, e.NomeFantasia, e.InscricaoMunicipal, e.CodigoMunicipio,
                e.Telefone, e.Email, e.Cep, e.Logradouro, e.Numero, e.Complemento, e.Bairro, e.Uf,
                e.OpSimpNac, e.RegApTribSN, e.RegEspTrib, e.TribIssqn, e.TpRetIssqn,
                e.CstPisCofins, e.TpRetPisCofins, e.PercentualTotalTributosSimplesNacional,
                e.DiasAlertaReajusteContratoPadrao, e.Ativo, e.CreatedAt, e.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<EmpresaResponse>
        {
            Items = itens,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}
