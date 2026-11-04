using NfseSaaS.Domain.Common;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Um evento do Histórico de valores do Contrato: valor inicial, reajuste
/// ou alteração de escopo. Responde "quanto este Contrato valia em cada
/// momento, e por quê". Append-only — um evento não é editado nem apagado
/// depois de Aplicado (correção = novo evento).
///
/// Evolução da antiga ReajusteContrato (tabela reajustes_contrato,
/// renomeada pela migration HistoricoValoresContrato): os reajustes já
/// registrados viraram eventos Tipo=Reajuste, sem perda de dados.
/// </summary>
public sealed class MovimentacaoValorContrato : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid ContratoId { get; set; }

    public TipoMovimentacaoValor Tipo { get; set; }

    public StatusMovimentacaoValor Status { get; set; } = StatusMovimentacaoValor.Aplicado;

    /// <summary>A partir de quando o valor novo vale.</summary>
    public DateOnly DataVigencia { get; set; }

    /// <summary>Nulo só em ValorInicial (não havia valor antes).</summary>
    public decimal? ValorAnterior { get; set; }

    public decimal ValorNovo { get; set; }

    /// <summary>Percentual que o índice deu (reajuste proposto pelo sistema). Nulo em registro manual.</summary>
    public decimal? PercentualCalculado { get; set; }

    /// <summary>Percentual efetivamente aplicado (pode ser menor que o calculado, se negociado).</summary>
    public decimal? PercentualAplicado { get; set; }

    public IndiceReajusteContrato? Indice { get; set; }

    /// <summary>Primeiro e último mês do índice usados no cálculo (reajuste por índice). Nulos em registro manual.</summary>
    public DateOnly? CompetenciaIndiceInicio { get; set; }

    public DateOnly? CompetenciaIndiceFim { get; set; }

    /// <summary>Por que mudou (obrigatório em alteração; observação livre no reajuste).</summary>
    public string? Motivo { get; set; }

    /// <summary>
    /// Linhas de serviço vigentes DEPOIS deste evento (JSON de
    /// ComposicaoContratoLinha[]). Nulo nos reajustes antigos, migrados de
    /// antes deste campo existir.
    /// </summary>
    public string? ComposicaoJson { get; set; }

    /// <summary>true = gerado/aplicado pelo sistema (reajuste automático); false = registrado por um usuário.</summary>
    public bool Automatico { get; set; }

    /// <summary>Quem aprovou/recusou um evento Pendente, e quando.</summary>
    public Guid? DecididoPorUsuarioId { get; set; }

    public DateTimeOffset? DecididoEm { get; set; }

    public const int TamanhoMaximoMotivo = 500;
}
