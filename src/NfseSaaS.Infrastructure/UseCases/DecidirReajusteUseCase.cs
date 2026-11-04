using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.ReajustesContrato;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Contratos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Decide um reajuste "aguardando aprovação". O percentual é aplicado sobre
/// o valor ATUAL do Contrato (não o da proposta): se houve uma alteração de
/// escopo no meio-tempo, o reajuste incide sobre o valor já alterado.
/// Recusar fecha o ciclo sem mudar o valor — o próximo reajuste fica para
/// o aniversário seguinte.
/// </summary>
public sealed class DecidirReajusteUseCase : IDecidirReajusteUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly ICurrentUser _currentUser;

    public DecidirReajusteUseCase(AppDbContext db, IAuditLogWriter auditLogWriter, ICurrentUser currentUser)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
        _currentUser = currentUser;
    }

    public async Task ExecutarAsync(Guid contratoId, Guid movimentacaoId, DecidirReajusteRequest request, CancellationToken cancellationToken)
    {
        var contrato = await _db.Contratos.FirstOrDefaultAsync(c => c.Id == contratoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Contrato {contratoId} não encontrado.");

        var evento = await _db.MovimentacoesValorContrato
            .FirstOrDefaultAsync(m => m.Id == movimentacaoId && m.ContratoId == contratoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Reajuste {movimentacaoId} não encontrado neste contrato.");

        if (evento.Status != StatusMovimentacaoValor.Pendente)
            throw new RegraNegocioException("Este reajuste já foi decidido.");

        var motivo = string.IsNullOrWhiteSpace(request.Motivo) ? null : request.Motivo.Trim();
        var valorAtual = contrato.ValorAtual;

        evento.DecididoPorUsuarioId = _currentUser.UserId;
        evento.DecididoEm = DateTimeOffset.UtcNow;
        evento.Automatico = false;

        switch (request.Acao)
        {
            case AcaoDecisaoReajuste.AplicarCalculado:
            {
                var percentual = ReajusteContrato.PercentualEfetivo(evento.PercentualCalculado ?? 0m, contrato.AplicarIndiceNegativo);
                if (motivo is not null) evento.Motivo = motivo;
                await ReajusteContrato.AplicarAsync(
                    _db, contrato, evento, ReajusteContrato.CalcularValor(valorAtual, percentual), percentual, cancellationToken);
                break;
            }

            case AcaoDecisaoReajuste.AplicarNegociado:
            {
                decimal valorNovo;
                decimal? percentual;
                if (request.ValorNovoNegociado is { } valor)
                {
                    valorNovo = Math.Round(valor, 2, MidpointRounding.AwayFromZero);
                    percentual = null; // derivado do valor em AplicarAsync
                }
                else
                {
                    percentual = request.PercentualNegociado!.Value;
                    valorNovo = ReajusteContrato.CalcularValor(valorAtual, percentual.Value);
                }

                evento.Motivo = motivo;
                await ReajusteContrato.AplicarAsync(_db, contrato, evento, valorNovo, percentual, cancellationToken);
                break;
            }

            case AcaoDecisaoReajuste.Recusar:
                // Nada muda no valor; o evento fica no histórico como "não
                // aplicado" com o que foi proposto, e o ciclo fecha.
                evento.Status = StatusMovimentacaoValor.Recusado;
                evento.Motivo = motivo;
                evento.ValorAnterior = valorAtual;
                contrato.DataUltimoReajuste = evento.DataVigencia;
                break;

            default:
                throw new RegraNegocioException("Ação inválida.");
        }

        _auditLogWriter.Registrar("DecidirReajusteContrato", "Contrato", contrato.Id,
            new { MovimentacaoId = evento.Id, request.Acao, evento.PercentualCalculado, evento.PercentualAplicado, ValorAnterior = valorAtual, evento.ValorNovo, Motivo = motivo });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
