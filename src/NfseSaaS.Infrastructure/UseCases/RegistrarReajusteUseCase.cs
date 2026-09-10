using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.ReajustesContrato;
using NfseSaaS.Domain.Entities;
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

        var reajuste = new ReajusteContrato
        {
            ContratoId = contratoId,
            DataReajuste = request.DataReajuste,
            ValorAnterior = valorAnterior,
            ValorNovo = request.ValorNovo,
            PercentualAplicado = percentualAplicado,
            IndiceUsado = request.IndiceUsado,
            Observacao = request.Observacao
        };

        contrato.ValorAtual = request.ValorNovo;
        contrato.DataUltimoReajuste = request.DataReajuste;

        _db.ReajustesContrato.Add(reajuste);

        _auditLogWriter.Registrar("RegistrarReajusteContrato", "Contrato", contrato.Id,
            new { valorAnterior, request.ValorNovo, percentualAplicado, request.IndiceUsado });

        await _db.SaveChangesAsync(cancellationToken);

        return reajuste.Id;
    }
}
