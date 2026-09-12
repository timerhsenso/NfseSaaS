using NfseSaaS.Domain.Common;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Registro de todo e-mail que o sistema tenta enviar (convite, reset de
/// senha, esqueci senha — e qualquer outro que vier a existir). Existe
/// por dois motivos ao mesmo tempo: (1) é o item da fila de envio em
/// segundo plano — grava Pendente antes de entrar na fila, pra não
/// perder o pedido se a aplicação reiniciar no meio (ver
/// EmailPendenteRecuperador); (2) é o que a tela de E-mails lista, com
/// status, detalhe do erro e reenvio.
///
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
/// </summary>
public sealed class EmailLog : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Código estável do tipo — ver TipoEmailCatalogo.</summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>ApplicationUser relacionado, quando aplicável (convite, reset de senha, esqueci senha sempre têm um) — Guid solto, sem FK real (mesmo padrão de AuditLog.UserId).</summary>
    public Guid? UsuarioId { get; set; }

    public string Destinatario { get; set; } = string.Empty;

    public string Assunto { get; set; } = string.Empty;

    /// <summary>Corpo exato que foi (ou vai ser) enviado — é o que "Reenviar" manda de novo, sem regenerar nada (token incluso, se houver, é o mesmo da tentativa original).</summary>
    public string CorpoHtml { get; set; } = string.Empty;

    public StatusEmailLog Status { get; set; } = StatusEmailLog.Pendente;

    /// <summary>Mensagem de erro da última tentativa — null enquanto Pendente/Enviado.</summary>
    public string? ErroDetalhe { get; set; }

    public int Tentativas { get; set; }

    public DateTimeOffset? EnviadoEm { get; set; }
}
