using Microsoft.EntityFrameworkCore;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Email;

/// <summary>
/// Quem recebe os avisos automáticos da plataforma. Centralizado para que
/// todo aviso (certificado vencendo, reajustes, alertas do operador) use a
/// mesma regra de "usuário ativo".
/// </summary>
public static class DestinatariosAviso
{
    /// <summary>
    /// Administradores ATIVOS da conta: grupo administrador, convite
    /// aceito (ou primeiro usuário, que não passou por convite) e sem
    /// bloqueio. AspNetUsers não tem filtro de tenant — o filtro aqui é
    /// manual; Grupos tem, então o Tenant precisa estar definido no escopo.
    /// </summary>
    public static Task<List<string>> AdministradoresDaContaAsync(AppDbContext db, Guid tenantId, CancellationToken cancellationToken) =>
        AdministradoresAsync(db, tenantId, apenasEmailConfirmado: false, cancellationToken);

    /// <summary>
    /// Como <see cref="AdministradoresDaContaAsync"/>, mas só quem já
    /// confirmou o e-mail (avisos de cobrança: falam de dinheiro e de
    /// bloqueio, então não vão para endereço nunca confirmado).
    /// </summary>
    public static Task<List<string>> AdministradoresConfirmadosDaContaAsync(AppDbContext db, Guid tenantId, CancellationToken cancellationToken) =>
        AdministradoresAsync(db, tenantId, apenasEmailConfirmado: true, cancellationToken);

    /// <summary>
    /// Como <see cref="AdministradoresConfirmadosDaContaAsync"/>, para quem
    /// roda FORA da conta (operador, job): ignora o filtro global e filtra a
    /// conta à mão nos usuários e nos grupos.
    /// </summary>
    public static async Task<List<string>> AdministradoresConfirmadosDeOutraContaAsync(AppDbContext db, Guid tenantId, CancellationToken cancellationToken)
    {
        var agora = DateTimeOffset.UtcNow;

        var emails = await (
            from u in db.Users.AsNoTracking()
            join g in db.Grupos.IgnoreQueryFilters().AsNoTracking() on u.GrupoId equals g.Id
            where u.TenantId == tenantId
                && g.TenantId == tenantId
                && g.EhAdministrador
                && u.Email != null
                && (u.ConvidadoEm == null || u.ConviteAceitoEm != null)
                && (u.LockoutEnd == null || u.LockoutEnd < agora)
                && u.EmailConfirmed
            select u.Email!).ToListAsync(cancellationToken);

        return Normalizar(emails);
    }

    private static async Task<List<string>> AdministradoresAsync(AppDbContext db, Guid tenantId, bool apenasEmailConfirmado, CancellationToken cancellationToken)
    {
        var agora = DateTimeOffset.UtcNow;

        var emails = await (
            from u in db.Users.AsNoTracking()
            join g in db.Grupos.AsNoTracking() on u.GrupoId equals g.Id
            where u.TenantId == tenantId
                && g.EhAdministrador
                && u.Email != null
                && (u.ConvidadoEm == null || u.ConviteAceitoEm != null)
                && (u.LockoutEnd == null || u.LockoutEnd < agora)
                && (!apenasEmailConfirmado || u.EmailConfirmed)
            select u.Email!).ToListAsync(cancellationToken);

        return Normalizar(emails);
    }

    /// <summary>Tira vazios e repetidos (sem diferenciar maiúsculas).</summary>
    public static List<string> Normalizar(IEnumerable<string?> emails) =>
        emails
            .Select(e => e?.Trim() ?? string.Empty)
            .Where(e => e.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
