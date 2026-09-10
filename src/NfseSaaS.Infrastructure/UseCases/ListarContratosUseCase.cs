using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ListarContratosUseCase : IListarContratosUseCase
{
    private readonly AppDbContext _db;

    public ListarContratosUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<ContratoResponse>> ExecutarAsync(ListarContratosRequest request, CancellationToken cancellationToken)
    {
        var empresa = await _db.Empresas.AsNoTracking().FirstOrDefaultAsync(e => e.Id == request.EmpresaId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Empresa {request.EmpresaId} não encontrada.");

        var (page, pageSize) = Paginacao.Normalizar(request.Page, request.PageSize);

        var query =
            from c in _db.Contratos.AsNoTracking()
            where c.EmpresaId == request.EmpresaId
            join cli in _db.Clientes.AsNoTracking() on c.ClienteId equals cli.Id
            join srv in _db.Servicos.AsNoTracking() on c.ServicoId equals srv.Id
            select new { Contrato = c, ClienteNome = cli.Nome, ServicoDescricao = srv.Descricao };

        if (!request.IncluirInativos)
            query = query.Where(x => x.Contrato.Ativo);

        if (request.ClienteId.HasValue)
            query = query.Where(x => x.Contrato.ClienteId == request.ClienteId.Value);

        if (!string.IsNullOrWhiteSpace(request.Busca))
        {
            var busca = request.Busca.Trim();
            query = query.Where(x =>
                EF.Functions.ILike(x.Contrato.Descricao, $"%{busca}%") ||
                EF.Functions.ILike(x.ClienteNome, $"%{busca}%"));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var itens = await query
            .OrderBy(x => x.ClienteNome).ThenBy(x => x.Contrato.Descricao)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);

        var respostas = itens.Select(x =>
        {
            var dataProximoReajuste = SituacaoContratoCalculator.CalcularDataProximoReajuste(x.Contrato);
            var diasAlertaEfetivo = SituacaoContratoCalculator.DiasAlertaEfetivo(x.Contrato, empresa.DiasAlertaReajusteContratoPadrao);
            var situacao = SituacaoContratoCalculator.CalcularSituacao(x.Contrato, empresa.DiasAlertaReajusteContratoPadrao, hoje);

            return new ContratoResponse(
                x.Contrato.Id, x.Contrato.EmpresaId, x.Contrato.ClienteId, x.ClienteNome,
                x.Contrato.ServicoId, x.ServicoDescricao, x.Contrato.Descricao, x.Contrato.ValorAtual,
                x.Contrato.DataInicioContrato, x.Contrato.PeriodicidadeReajusteMeses, x.Contrato.IndiceReajuste,
                x.Contrato.DataUltimoReajuste, x.Contrato.DiasAlertaOverride, diasAlertaEfetivo,
                dataProximoReajuste, situacao, x.Contrato.Ativo, x.Contrato.CreatedAt, x.Contrato.UpdatedAt);
        }).ToList();

        return new PagedResult<ContratoResponse>
        {
            Items = respostas,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}
