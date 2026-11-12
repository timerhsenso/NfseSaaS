using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.HistoricoContrato;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
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
        ["AnexarDocumentoContrato"] = "Documento anexado",
        ["ExcluirDocumentoContrato"] = "Documento excluído"
    };

    // Operações cujo conteúdo já aparece pelo Histórico de valores
    // (MovimentacaoValorContrato) — o AuditLog delas continua gravado, mas
    // não entra na timeline pra não mostrar o mesmo fato duas vezes.
    private static readonly HashSet<string> OperacoesCobertasPeloHistoricoDeValores = new()
    {
        "RegistrarReajusteContrato",
        "AlterarServicosContrato",
        "DesfazerReajusteContrato"
    };

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

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
            .Where(a => !OperacoesCobertasPeloHistoricoDeValores.Contains(a.Operacao))
            .Select(a => new HistoricoContratoItemResponse(a.DataHora, "AuditLog", RotulosOperacao.GetValueOrDefault(a.Operacao, a.Operacao)))
            .ToList();

        var eventosValor = await _db.MovimentacoesValorContrato.AsNoTracking()
            .Where(m => m.ContratoId == contratoId)
            .ToListAsync(cancellationToken);

        var valores = eventosValor
            .Select(m => new HistoricoContratoItemResponse(m.CreatedAt, "Reajuste", Descrever(m)))
            .ToList();

        return logs.Concat(valores).OrderByDescending(i => i.Data).ToList();
    }

    private static string Descrever(MovimentacaoValorContrato m)
    {
        string Reais(decimal? v) => v is null ? "-" : "R$ " + v.Value.ToString("N2", PtBr);
        var faixa = $"{Reais(m.ValorAnterior)} → {Reais(m.ValorNovo)}";
        var percentual = m.PercentualAplicado is { } p ? $" ({p.ToString("N2", PtBr)}%)" : string.Empty;
        var situacao = m.Status switch
        {
            StatusMovimentacaoValor.Pendente => " — aguardando aprovação",
            StatusMovimentacaoValor.Recusado => " — não aplicado",
            StatusMovimentacaoValor.Desfeito => " — desfeito" + (m.MotivoDesfazer is null ? string.Empty : $": {m.MotivoDesfazer}"),
            _ => string.Empty
        };
        var vigencia = $" (vigência {m.DataVigencia.ToString("dd/MM/yyyy", PtBr)})";

        return m.Tipo switch
        {
            TipoMovimentacaoValor.ValorInicial => $"Valor inicial: {Reais(m.ValorNovo)}{vigencia}",
            TipoMovimentacaoValor.Alteracao => $"Alteração de escopo: {faixa}{vigencia}" + (m.Motivo is null ? string.Empty : $" — {m.Motivo}"),
            _ => $"Reajuste: {faixa}{percentual}{vigencia}{situacao}"
        };
    }
}
