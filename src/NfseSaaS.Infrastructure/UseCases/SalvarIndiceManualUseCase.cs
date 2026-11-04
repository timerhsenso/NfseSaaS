using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.IndicesEconomicos;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class SalvarIndiceManualUseCase : ISalvarIndiceManualUseCase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public SalvarIndiceManualUseCase(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task ExecutarAsync(SalvarIndiceManualRequest request, CancellationToken cancellationToken)
    {
        var competencia = IndiceEconomicoMensal.NormalizarCompetencia(request.Competencia);

        var existente = await _db.IndicesEconomicosMensais
            .FirstOrDefaultAsync(i => i.Indice == request.Indice && i.Competencia == competencia, cancellationToken);

        if (existente is null)
        {
            _db.IndicesEconomicosMensais.Add(new IndiceEconomicoMensal
            {
                Indice = request.Indice,
                Competencia = competencia,
                Variacao = request.Variacao,
                Fonte = FonteIndiceEconomico.Manual,
                AtualizadoPorUsuarioId = _currentUser.UserId
            });
        }
        else
        {
            existente.Variacao = request.Variacao;
            existente.Fonte = FonteIndiceEconomico.Manual;
            existente.AtualizadoPorUsuarioId = _currentUser.UserId;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
