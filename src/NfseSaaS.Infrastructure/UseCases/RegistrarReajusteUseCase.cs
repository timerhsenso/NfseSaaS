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
///
/// Dois reajustes dentro do mesmo ciclo pedem uma escolha (ver
/// <see cref="RegistrarReajusteRequest"/>): substituir o anterior (desfaz e
/// aplica este, tudo na mesma gravação) ou confirmar que é mais um.
/// </summary>
public sealed class RegistrarReajusteUseCase : IRegistrarReajusteUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    private readonly ICurrentUser _currentUser;

    public RegistrarReajusteUseCase(AppDbContext db, IAuditLogWriter auditLogWriter, ICurrentUser currentUser)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
        _currentUser = currentUser;
    }

    public async Task<Guid> ExecutarAsync(Guid contratoId, RegistrarReajusteRequest request, CancellationToken cancellationToken)
    {
        var contrato = await _db.Contratos.FirstOrDefaultAsync(c => c.Id == contratoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Contrato {contratoId} não encontrado.");

        await ReajusteContrato.GarantirSemPendenteAsync(_db, contratoId, cancellationToken);

        decimal? valorSubstituido = null;
        if (request.SubstituirReajusteId is { } substituirId)
        {
            var anterior = await _db.MovimentacoesValorContrato
                .FirstOrDefaultAsync(m => m.Id == substituirId && m.ContratoId == contratoId, cancellationToken)
                ?? throw new RecursoNaoEncontradoException($"Reajuste {substituirId} não encontrado neste contrato.");

            valorSubstituido = contrato.ValorAtual;
            await DesfazerReajusteContrato.DesfazerAsync(
                _db, contrato, anterior, request.MotivoSubstituicao, _currentUser.UserId, cancellationToken);
        }
        else if (!request.ConfirmarReajusteRecente)
        {
            var eventos = await _db.MovimentacoesValorContrato.AsNoTracking()
                .Where(m => m.ContratoId == contratoId)
                .ToListAsync(cancellationToken);
            if (RegrasDesfazerReajuste.ReajusteRecente(eventos, request.DataReajuste, contrato.PeriodicidadeReajusteMeses) is { } recente)
                throw new RegraNegocioException(RegrasDesfazerReajuste.MensagemReajusteRecente(recente, contrato.PeriodicidadeReajusteMeses));
        }

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
            new { valorAnterior, request.ValorNovo, reajuste.PercentualAplicado, request.IndiceUsado, request.SubstituirReajusteId, valorSubstituido });

        await _db.SaveChangesAsync(cancellationToken);

        return reajuste.Id;
    }
}
