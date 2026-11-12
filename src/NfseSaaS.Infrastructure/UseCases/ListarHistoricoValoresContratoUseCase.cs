using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.HistoricoValoresContrato;
using NfseSaaS.Infrastructure.Contratos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ListarHistoricoValoresContratoUseCase : IListarHistoricoValoresContratoUseCase
{
    private readonly AppDbContext _db;

    public ListarHistoricoValoresContratoUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<MovimentacaoValorContratoResponse>> ExecutarAsync(Guid contratoId, CancellationToken cancellationToken)
    {
        var contrato = await _db.Contratos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contratoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Contrato {contratoId} não encontrado.");

        // Materializa antes de mapear: a composição é JSON desserializado em
        // memória (não traduz pra SQL).
        var eventos = await _db.MovimentacoesValorContrato.AsNoTracking()
            .Where(m => m.ContratoId == contratoId)
            .OrderByDescending(m => m.DataVigencia).ThenByDescending(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        // Só o último reajuste aplicado pode ser desfeito; os outros nem
        // mostram o botão.
        var (desfazivel, bloqueio) = await DesfazerReajusteContrato.AvaliarAsync(_db, contrato, eventos, cancellationToken);

        // Quem desfez: o e-mail do usuário (a tela mostra "por fulano").
        var usuarioIds = eventos.Where(m => m.DesfeitoPorUsuarioId.HasValue).Select(m => m.DesfeitoPorUsuarioId!.Value).Distinct().ToList();
        var emails = usuarioIds.Count == 0
            ? new Dictionary<Guid, string?>()
            : await _db.Users.AsNoTracking()
                .Where(u => usuarioIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);

        return eventos
            .Select(m => new MovimentacaoValorContratoResponse(
                m.Id, m.ContratoId, m.Tipo, m.Status, m.DataVigencia, m.ValorAnterior, m.ValorNovo,
                m.PercentualCalculado, m.PercentualAplicado, m.Indice, m.CompetenciaIndiceInicio, m.CompetenciaIndiceFim,
                m.Motivo, ComposicaoContrato.Desserializar(m.ComposicaoJson), m.Automatico, m.CreatedAt,
                m.DesfeitoEm,
                m.DesfeitoPorUsuarioId is { } quem ? emails.GetValueOrDefault(quem) : null,
                m.MotivoDesfazer,
                PodeDesfazer: desfazivel?.Id == m.Id && bloqueio is null,
                BloqueioDesfazer: desfazivel?.Id == m.Id ? bloqueio : null))
            .ToList();
    }
}
