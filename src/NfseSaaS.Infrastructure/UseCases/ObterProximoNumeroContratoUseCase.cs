using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Infrastructure.Contratos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ObterProximoNumeroContratoUseCase : IObterProximoNumeroContratoUseCase
{
    private readonly AppDbContext _db;

    public ObterProximoNumeroContratoUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<string> ExecutarAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        if (!await _db.Empresas.AnyAsync(e => e.Id == empresaId, cancellationToken))
            throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");

        return await NumeracaoContrato.ProximoAsync(_db, empresaId, DataBrasilia.Hoje().Year, cancellationToken);
    }
}
