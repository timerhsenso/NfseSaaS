using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.HistoricoContrato;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Não é uma entidade nova — junta AuditLog (genérico, toda escrita já
/// registrada) filtrado por Entidade="Contrato"+EntidadeId, com
/// ReajusteContrato (histórico específico, já tem tela própria), numa
/// timeline só ordenada por data. Decisão do usuário: reaproveitar em
/// vez de criar changelog estruturado campo a campo.
/// </summary>
public sealed class ObterHistoricoContratoUseCase : IObterHistoricoContratoUseCase
{
    private static readonly Dictionary<string, string> RotulosOperacao = new()
    {
        ["CadastrarContrato"] = "Contrato cadastrado",
        ["AtualizarContrato"] = "Contrato atualizado",
        ["DesativarContrato"] = "Contrato desativado",
        ["ReativarContrato"] = "Contrato reativado",
        ["RegistrarReajusteContrato"] = "Reajuste registrado",
        ["AnexarDocumentoContrato"] = "Documento anexado",
        ["ExcluirDocumentoContrato"] = "Documento excluído"
    };

    private readonly AppDbContext _db;

    public ObterHistoricoContratoUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<HistoricoContratoItemResponse>> ExecutarAsync(Guid contratoId, CancellationToken cancellationToken)
    {
        var contratoExiste = await _db.Contratos.AnyAsync(c => c.Id == contratoId, cancellationToken);
        if (!contratoExiste)
            throw new RecursoNaoEncontradoException($"Contrato {contratoId} não encontrado.");

        var logsBrutos = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.Entidade == "Contrato" && a.EntidadeId == contratoId)
            .Select(a => new { a.DataHora, a.Operacao })
            .ToListAsync(cancellationToken);

        var logs = logsBrutos
            .Select(a => new HistoricoContratoItemResponse(a.DataHora, "AuditLog", RotulosOperacao.GetValueOrDefault(a.Operacao, a.Operacao)))
            .ToList();

        var reajustes = await _db.ReajustesContrato.AsNoTracking()
            .Where(r => r.ContratoId == contratoId)
            .Select(r => new HistoricoContratoItemResponse(
                r.CreatedAt,
                "Reajuste",
                $"Reajuste: R$ {r.ValorAnterior:N2} → R$ {r.ValorNovo:N2}" + (r.PercentualAplicado != null ? $" ({r.PercentualAplicado:N2}%)" : string.Empty)))
            .ToListAsync(cancellationToken);

        return logs.Concat(reajustes).OrderByDescending(i => i.Data).ToList();
    }
}
