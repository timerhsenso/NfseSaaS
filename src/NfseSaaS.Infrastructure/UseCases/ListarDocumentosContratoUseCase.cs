using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.UseCases.DocumentosContrato;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ListarDocumentosContratoUseCase : IListarDocumentosContratoUseCase
{
    private readonly AppDbContext _db;

    public ListarDocumentosContratoUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ContratoDocumentoResponse>> ExecutarAsync(Guid contratoId, CancellationToken cancellationToken)
    {
        return await _db.ContratoDocumentos.AsNoTracking()
            .Where(d => d.ContratoId == contratoId)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new ContratoDocumentoResponse(d.Id, d.ContratoId, d.NomeOriginal, d.Extensao, d.TamanhoBytes, d.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
