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

    /// <summary>
    /// Nota fiscal anexada (e-mail da nota ao tomador). Os anexos — PDF
    /// (DANFSe) e XML — NÃO ficam guardados aqui: são gerados de novo da
    /// própria Nfse a cada tentativa de envio (inclusive no "Reenviar"),
    /// então o log não engorda com arquivos. Guid solto, sem FK (mesmo
    /// padrão de UsuarioId).
    /// </summary>
    public Guid? NfseId { get; set; }

    /// <summary>
    /// Identifica um aviso que só pode sair UMA vez (ex.: "certificado da
    /// empresa X, validade Y, faltando 15 dias"). Quem enfileira confere se
    /// já existe um EmailLog com a mesma chave antes de mandar de novo.
    /// </summary>
    public string? ChaveDeduplicacao { get; set; }

    /// <summary>
    /// Endereço do "Responder para" (Reply-To). Ex.: convite de indicação —
    /// a resposta vai para quem indicou, não para o remetente da plataforma.
    /// Nulo = sem Reply-To.
    /// </summary>
    public string? ResponderPara { get; set; }

    /// <summary>
    /// Endereços em cópia, separados por ";" (ex.: e-mail da nota com as
    /// cópias do cadastro do cliente). Nulo = sem cópia. O "Reenviar"
    /// manda de novo para eles também.
    /// </summary>
    public string? Copias { get; set; }

    /// <summary>true = Copias vão em Cco (ocultas); false = Cc.</summary>
    public bool CopiasOcultas { get; set; }

    /// <summary>
    /// Cópia oculta fixa, sempre em Cco, além das Copias (ex.: o escritório
    /// que guarda toda nota enviada — modelo do e-mail da nota). Nulo = sem.
    /// </summary>
    public string? CopiaOcultaFixa { get; set; }
}
