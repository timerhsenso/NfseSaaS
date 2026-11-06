using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Convite por e-mail enviado pela tela "Indique e ganhe". Existe para os
/// limites anti-spam (até 10 por dia por conta; o mesmo endereço recebe no
/// máximo 1 convite a cada 30 dias, de qualquer conta) e para a conta ver
/// os convites que mandou. Entidade TENANT-SCOPED (conta que convidou).
/// </summary>
public sealed class ConviteIndicacao : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>E-mail convidado, normalizado (minúsculas, sem espaços).</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Usuário que enviou (o Reply-To do convite é o e-mail dele). Guid solto, sem FK.</summary>
    public Guid EnviadoPorUsuarioId { get; set; }

    /// <summary>EmailLog do envio (status na tela "E-mails enviados"). Guid solto, sem FK.</summary>
    public Guid? EmailLogId { get; set; }
}
