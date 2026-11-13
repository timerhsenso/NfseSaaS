using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.Tour;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>TR1: ver ITourUseCase. Sempre o próprio usuário logado.</summary>
public sealed class TourService : ITourUseCase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public TourService(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<string>> VistosAsync(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } usuarioId)
            return Array.Empty<string>();
        var gravado = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == usuarioId)
            .Select(u => u.ToursVistos)
            .FirstOrDefaultAsync(cancellationToken);
        return RegrasTour.Ler(gravado);
    }

    public async Task MarcarVistoAsync(string? codigo, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } usuarioId)
            return;
        var usuario = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == usuarioId, cancellationToken);
        if (usuario is null)
            return;
        var novo = RegrasTour.Marcar(usuario.ToursVistos, codigo);
        if (novo == usuario.ToursVistos)
            return;
        usuario.ToursVistos = novo;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReverTodosAsync(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } usuarioId)
            return;
        await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == usuarioId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.ToursVistos, (string?)null), cancellationToken);
    }
}
