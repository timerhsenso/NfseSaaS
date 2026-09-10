using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Enums;
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
        {
            query = query.Where(n => n.EmpresaId == request.EmpresaId.Value);

            // A lista só mostra notas do ambiente em que a Empresa está
            // AGORA (Homologação ou Produção) — trocar o ambiente da
            // Empresa muda o que aparece aqui, sem precisar de nenhum
            // filtro adicional na tela. Cada Nfse guarda o ambiente em
            // que nasceu (ver Nfse.TipoAmbiente), então essa consulta
            // busca o TipoAmbiente atual da Empresa só pra filtrar — não
            // altera nem lê nada das notas em si além do que já viria.
            var tipoAmbienteEmpresa = await _db.Empresas.AsNoTracking()
                .Where(e => e.Id == request.EmpresaId.Value)
                .Select(e => (TipoAmbiente?)e.TipoAmbiente)
                .FirstOrDefaultAsync(cancellationToken);

            if (tipoAmbienteEmpresa is not null)
                query = query.Where(n => n.TipoAmbiente == tipoAmbienteEmpresa.Value);
        }

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
                n.TipoAmbiente, n.CodigoErro, n.MensagemErro, n.CreatedAt, n.UpdatedAt))
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
