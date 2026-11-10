using NfseSaaS.Domain.Common;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Reajuste do preço de tabela para quem já assina (B2, Termos 1.1 item 8.5):
/// aviso por e-mail com pelo menos 30 dias, no máximo um por ano por
/// assinatura, preço de fundador fora. Uma linha por assinatura; as linhas
/// criadas no mesmo clique do operador têm o mesmo <see cref="LoteId"/>.
/// Entidade GLOBAL (como a Assinatura): o job aplica sem usuário logado.
/// </summary>
public sealed class ReajusteAssinatura : BaseEntity
{
    public Guid TenantId { get; set; }

    public Guid AssinaturaId { get; set; }

    /// <summary>Agendamento do operador que criou esta linha (cancelar = o lote todo).</summary>
    public Guid LoteId { get; set; }

    /// <summary>Código do plano (Plano.Codigo) na hora do aviso: trocou de plano, o reajuste não vale.</summary>
    public int Plano { get; set; }

    /// <summary>Valor mensal que a assinatura pagava no aviso.</summary>
    public decimal PrecoAnterior { get; set; }

    /// <summary>Preço de tabela do plano em <see cref="AplicarEm"/>.</summary>
    public decimal PrecoNovo { get; set; }

    /// <summary>Dia em que o novo valor entra (faturas com vencimento a partir dele).</summary>
    public DateOnly AplicarEm { get; set; }

    public StatusReajusteAssinatura Status { get; set; } = StatusReajusteAssinatura.Agendado;

    /// <summary>Quando o aviso foi para a fila de e-mail (nulo = sem administrador com e-mail confirmado).</summary>
    public DateTimeOffset? AvisadoEm { get; set; }

    public DateTimeOffset? AplicadoEm { get; set; }

    /// <summary>Motivo do operador ao agendar.</summary>
    public string? Motivo { get; set; }

    /// <summary>Por que foi cancelado ou o último erro do Asaas.</summary>
    public string? Detalhe { get; set; }

    public int Tentativas { get; set; }

    public Guid? CriadoPorUsuarioId { get; set; }
}
