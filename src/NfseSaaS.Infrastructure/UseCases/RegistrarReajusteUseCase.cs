using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.ReajustesContrato;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Contratos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Único caminho previsto pra mudar Contrato.ValorAtual depois de criado
/// (ver comentário em AtualizarContratoRequest) — grava o histórico
/// (ReajusteContrato) E atualiza o Contrato no mesmo SaveChangesAsync,
/// então as duas gravações são atômicas: nunca fica um histórico
/// registrado sem o Contrato refletir o novo valor, nem o contrário.
/// </summary>
public sealed class RegistrarReajusteUseCase : IRegistrarReajusteUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public RegistrarReajusteUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<Guid> ExecutarAsync(Guid contratoId, RegistrarReajusteRequest request, CancellationToken cancellationToken)
    {
        var contrato = await _db.Contratos.FirstOrDefaultAsync(c => c.Id == contratoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Contrato {contratoId} não encontrado.");

        var valorAnterior = contrato.ValorAtual;

        // Percentual só informativo (sempre derivável de ValorAnterior/
        // ValorNovo) — calculado aqui pra não recalcular/arredondar
        // diferente em cada lugar que for exibir isso depois.
        var percentualAplicado = valorAnterior == 0
            ? (decimal?)null
            : Math.Round((request.ValorNovo - valorAnterior) / valorAnterior * 100, 4);

        contrato.ValorAtual = request.ValorNovo;
        contrato.DataUltimoReajuste = request.DataReajuste;

        // Fase 6: ValorAtual é a soma das linhas de ContratoServico —
        // reajuste continua sendo um único percentual pro Contrato
        // inteiro (fluxo já testado em produção), então escala cada
        // linha pelo mesmo fator pra manter soma(linhas) == ValorAtual.
        // Limitação conhecida: arredondamento por linha pode deixar a
        // soma com centavos de diferença do total — aceitável pro MVP.
        var linhas = await _db.ContratoServicos.Where(cs => cs.ContratoId == contratoId).ToListAsync(cancellationToken);
        if (valorAnterior != 0)
        {
            var fator = request.ValorNovo / valorAnterior;
            foreach (var linha in linhas)
                linha.ValorUnitario = Math.Round(linha.ValorUnitario * fator, 2, MidpointRounding.AwayFromZero);
        }

        // Evento do Histórico de valores, com a composição já reajustada.
        var reajuste = new MovimentacaoValorContrato
        {
            ContratoId = contratoId,
            Tipo = TipoMovimentacaoValor.Reajuste,
            Status = StatusMovimentacaoValor.Aplicado,
            DataVigencia = request.DataReajuste,
            ValorAnterior = valorAnterior,
            ValorNovo = request.ValorNovo,
            PercentualAplicado = percentualAplicado,
            Indice = request.IndiceUsado,
            Motivo = request.Observacao,
            ComposicaoJson = await ComposicaoContrato.SerializarAsync(
                _db, linhas.Select(l => (l.ServicoId, l.Quantidade, l.ValorUnitario)), cancellationToken)
        };

        _db.MovimentacoesValorContrato.Add(reajuste);

        _auditLogWriter.Registrar("RegistrarReajusteContrato", "Contrato", contrato.Id,
            new { valorAnterior, request.ValorNovo, percentualAplicado, request.IndiceUsado });

        await _db.SaveChangesAsync(cancellationToken);

        return reajuste.Id;
    }
}
