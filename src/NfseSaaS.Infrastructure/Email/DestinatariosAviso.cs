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
    public static async Task<List<string>> AdministradoresDaContaAsync(AppDbContext db, Guid tenantId, CancellationToken cancellationToken)
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
