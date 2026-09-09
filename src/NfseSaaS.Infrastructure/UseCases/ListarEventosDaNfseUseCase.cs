using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.NfseEventos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Só leitura — não há endpoint de escrita para NfseEvento, ele é gravado
/// exclusivamente por INfseEventoWriter (ver Infrastructure.Auditing)
/// como efeito colateral de EmitirNfseUseCase/CancelarNfseUseCase, nunca
/// diretamente pela API.
/// </summary>
public sealed class ListarEventosDaNfseUseCase : IListarEventosDaNfseUseCase
{
    private readonly AppDbContext _db;

    public ListarEventosDaNfseUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<NfseEventoResponse>> ExecutarAsync(Guid nfseId, CancellationToken cancellationToken)
    {
        var nfseExiste = await _db.NotasFiscais.AsNoTracking().AnyAsync(n => n.Id == nfseId, cancellationToken);
        if (!nfseExiste)
            throw new RecursoNaoEncontradoException($"Nfse {nfseId} não encontrada.");

        return await _db.NfseEventos
            .AsNoTracking()
            .Where(e => e.NfseId == nfseId)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new NfseEventoResponse(
                e.Id, e.NfseId, e.Tipo.ToString(), e.Codigo, e.Mensagem, e.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
