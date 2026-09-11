using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.NotaMensal;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ListarCandidatosNotaMensalUseCase : IListarCandidatosNotaMensalUseCase
{
    private readonly AppDbContext _db;

    public ListarCandidatosNotaMensalUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ContratoCandidatoNotaMensalResponse>> ExecutarAsync(Guid empresaId, DateOnly competencia, CancellationToken cancellationToken)
    {
        var empresaExiste = await _db.Empresas.AnyAsync(e => e.Id == empresaId, cancellationToken);
        if (!empresaExiste)
            throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");

        var contratos = await (
            from c in _db.Contratos.AsNoTracking()
            where c.EmpresaId == empresaId && c.Ativo && c.TipoCobranca == TipoCobrancaContrato.Mensal && c.Status == StatusContrato.Ativo
            join cli in _db.Clientes.AsNoTracking() on c.ClienteId equals cli.Id
            select new { Contrato = c, ClienteNome = cli.Nome })
            .ToListAsync(cancellationToken);

        var contratoIds = contratos.Select(x => x.Contrato.Id).ToList();

        var linhasPorContrato = await (
            from cs in _db.ContratoServicos.AsNoTracking()
            where contratoIds.Contains(cs.ContratoId)
            join srv in _db.Servicos.AsNoTracking() on cs.ServicoId equals srv.Id
            select new { cs.ContratoId, Linha = cs, srv.Descricao, srv.CodigoTributacaoNacional })
            .ToListAsync(cancellationToken);

        // "Já emitido nesta competência" olha pro mês inteiro (não o dia
        // exato) — a nota mensal desse contrato pode ter sido emitida em
        // qualquer dia daquele mês, avulsa ou por este mesmo lote antes.
        var inicioMes = new DateOnly(competencia.Year, competencia.Month, 1);
        var fimMes = inicioMes.AddMonths(1).AddDays(-1);

        var contratosComNotaNaCompetencia = await _db.NotasFiscais.AsNoTracking()
            .Where(n => n.ContratoId != null && contratoIds.Contains(n.ContratoId.Value)
                && n.DataCompetencia >= inicioMes && n.DataCompetencia <= fimMes)
            .Select(n => n.ContratoId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var setComNota = contratosComNotaNaCompetencia.ToHashSet();

        return contratos.Select(x =>
        {
            var linhas = linhasPorContrato
                .Where(l => l.ContratoId == x.Contrato.Id)
                .Select(l => new LinhaCandidataNotaMensalResponse(
                    l.Linha.ServicoId, l.Descricao, l.CodigoTributacaoNacional, l.Linha.Quantidade, l.Linha.ValorUnitario, l.Linha.ValorTotal))
                .ToList();

            return new ContratoCandidatoNotaMensalResponse(
                x.Contrato.Id, x.Contrato.ClienteId, x.ClienteNome, x.Contrato.Descricao,
                linhas, x.Contrato.ValorAtual, x.Contrato.PermitirAlterarValorNaEmissao,
                setComNota.Contains(x.Contrato.Id));
        }).ToList();
    }
}
