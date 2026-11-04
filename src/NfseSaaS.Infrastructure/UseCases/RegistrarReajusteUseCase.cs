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
/// Reajuste MANUAL (valor digitado). Grava o evento do Histórico de valores
/// e atualiza o Contrato no mesmo SaveChangesAsync — atômico. A regra de
/// aplicar (escalar linhas, composição, data do último reajuste) é a mesma
/// do reajuste automático: ver <see cref="ReajusteContrato"/>.
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

        await ReajusteContrato.GarantirSemPendenteAsync(_db, contratoId, cancellationToken);

        var valorAnterior = contrato.ValorAtual;

        var reajuste = new MovimentacaoValorContrato
        {
            ContratoId = contratoId,
            Tipo = TipoMovimentacaoValor.Reajuste,
            DataVigencia = request.DataReajuste,
            Indice = request.IndiceUsado,
            Motivo = request.Observacao
        };

        await ReajusteContrato.AplicarAsync(_db, contrato, reajuste, request.ValorNovo, null, cancellationToken);

        _db.MovimentacoesValorContrato.Add(reajuste);

        _auditLogWriter.Registrar("RegistrarReajusteContrato", "Contrato", contrato.Id,
            new { valorAnterior, request.ValorNovo, reajuste.PercentualAplicado, request.IndiceUsado });

        await _db.SaveChangesAsync(cancellationToken);

        return reajuste.Id;
    }
}
