using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Um registro de reajuste aplicado a um Contrato — histórico
/// append-only (nunca editado/excluído depois de criado). Cada registro
/// também atualiza Contrato.ValorAtual/DataUltimoReajuste no mesmo
/// instante (ver RegistrarReajusteUseCase) — é o ÚNICO caminho previsto
/// pra mudar o valor de um Contrato depois de criado, de propósito (ver
/// comentário em AtualizarContratoRequest).
///
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
/// </summary>
public sealed class ReajusteContrato : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid ContratoId { get; set; }

    /// <summary>Data em que o reajuste passou a valer (não necessariamente "hoje" — pode ser retroativa ou combinada pra uma data específica).</summary>
    public DateOnly DataReajuste { get; set; }

    public decimal ValorAnterior { get; set; }

    public decimal ValorNovo { get; set; }

    /// <summary>Percentual de variação entre ValorAnterior e ValorNovo — só informativo (sempre derivável dos dois valores), guardado pra não recalcular/arredondar diferente em cada lugar que exibir.</summary>
    public decimal? PercentualAplicado { get; set; }

    /// <summary>Índice de referência usado NESTE reajuste específico — pode diferir do índice padrão do Contrato (ex.: negociação pontual num ano atípico).</summary>
    public string? IndiceUsado { get; set; }

    public string? Observacao { get; set; }
}
