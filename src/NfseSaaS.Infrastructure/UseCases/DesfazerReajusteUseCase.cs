using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.ReajustesContrato;
using NfseSaaS.Infrastructure.Contratos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// "Desfazer" do Histórico de valores: volta o Contrato ao que era antes do
/// último reajuste (ver <see cref="DesfazerReajusteContrato"/>). O evento
/// fica no histórico como Desfeito e a operação vai para a auditoria.
/// </summary>
public sealed class DesfazerReajusteUseCase : IDesfazerReajusteUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly ICurrentUser _currentUser;

    public DesfazerReajusteUseCase(AppDbContext db, IAuditLogWriter auditLogWriter, ICurrentUser currentUser)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
        _currentUser = currentUser;
    }

    public async Task ExecutarAsync(Guid contratoId, Guid movimentacaoId, DesfazerReajusteRequest request, CancellationToken cancellationToken)
    {
        var contrato = await _db.Contratos.FirstOrDefaultAsync(c => c.Id == contratoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Contrato {contratoId} não encontrado.");

        var reajuste = await _db.MovimentacoesValorContrato
            .FirstOrDefaultAsync(m => m.Id == movimentacaoId && m.ContratoId == contratoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Reajuste {movimentacaoId} não encontrado neste contrato.");

        var valorReajustado = contrato.ValorAtual;
        await DesfazerReajusteContrato.DesfazerAsync(_db, contrato, reajuste, request.Motivo, _currentUser.UserId, cancellationToken);

        _auditLogWriter.Registrar("DesfazerReajusteContrato", "Contrato", contrato.Id,
            new { MovimentacaoId = reajuste.Id, reajuste.DataVigencia, ValorDesfeito = valorReajustado, ValorRestaurado = contrato.ValorAtual, Motivo = reajuste.MotivoDesfazer });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
