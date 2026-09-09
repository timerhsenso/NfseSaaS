using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Snapshots;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ObterSnapshotFiscalDaNfseUseCase : IObterSnapshotFiscalDaNfseUseCase
{
    private readonly AppDbContext _db;

    public ObterSnapshotFiscalDaNfseUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<NfseSnapshotFiscal?> ExecutarAsync(Guid nfseId, CancellationToken cancellationToken)
    {
        var snapshotJson = await _db.NotasFiscais
            .AsNoTracking()
            .Where(n => n.Id == nfseId)
            .Select(n => n.SnapshotFiscalJson)
            .FirstOrDefaultAsync(cancellationToken);

        if (snapshotJson is null)
        {
            var existe = await _db.NotasFiscais.AsNoTracking().AnyAsync(n => n.Id == nfseId, cancellationToken);
            if (!existe)
                throw new RecursoNaoEncontradoException($"Nfse {nfseId} não encontrada.");

            // Nfse existe, mas foi emitida antes deste campo existir (ou
            // ainda está em Rascunho/Processando, sem snapshot gravado).
            return null;
        }

        return JsonSerializer.Deserialize<NfseSnapshotFiscal>(snapshotJson);
    }
}
