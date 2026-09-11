using Microsoft.AspNetCore.Identity;

namespace NfseSaaS.Infrastructure.Identity;

/// <summary>
/// Usuário da aplicação. Estende o Identity padrão com o TenantId ao qual
/// o usuário pertence — futuramente incluído nos Claims no login para
/// resolução do ICurrentTenant (ver NfseSaaS.Infrastructure.MultiTenancy).
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }

    /// <summary>
    /// Preenchido apenas para usuários criados via convite
    /// (AuthController.Convidar) — permanece null para o primeiro usuário
    /// (Administrador) de cada Tenant, criado via Registrar. É o que
    /// distingue "convite" de "conta normal" na listagem de convites —
    /// não dá pra usar só a existência do usuário pra isso.
    /// </summary>
    public DateTimeOffset? ConvidadoEm { get; set; }

    /// <summary>
    /// Preenchido quando o convite é efetivamente aceito
    /// (AuthController.AceitarConvite). Null enquanto pendente — é o
    /// campo que define o Status (Pendente/Aceito) na listagem de
    /// convites e bloqueia reenvio/exclusão de convite já aceito.
    /// </summary>
    public DateTimeOffset? ConviteAceitoEm { get; set; }
}
