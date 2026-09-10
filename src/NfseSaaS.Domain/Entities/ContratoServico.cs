using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Uma linha de serviço dentro de um Contrato — Fase 6 do roadmap:
/// decisão fechada de migrar Contrato de "1 serviço só" pra "N serviços",
/// pra suportar contratos que cobram mais de um serviço na mesma nota
/// mensal (Fase 7, Nota Mensal) e permitir agrupar por
/// CodigoTributacaoNacional na hora de gerar as notas.
///
/// Contrato.ValorAtual continua existindo como o total agregado (soma de
/// ContratoServico.ValorTotal) — decisão deliberada pra não quebrar o
/// fluxo de Reajuste (RegistrarReajusteUseCase) já testado em produção,
/// que grava histórico em cima de um valor único do Contrato. Um
/// reajuste escala proporcionalmente o ValorUnitario de cada linha (ver
/// RegistrarReajusteUseCase).
///
/// Composição das linhas (adicionar/remover/trocar Serviço) só acontece
/// no cadastro do Contrato nesta fase — AtualizarContratoUseCase
/// deliberadamente não mexe nas linhas, mesmo raciocínio já usado pro
/// ValorAtual (editar fora do fluxo dedicado pularia consistência). Um
/// endpoint dedicado pra recompor linhas depois de criado fica pra
/// quando houver demanda real, mesmo padrão do resto do projeto.
///
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
/// </summary>
public sealed class ContratoServico : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid ContratoId { get; set; }

    /// <summary>Serviço do catálogo de onde vem a classificação fiscal (código de tributação nacional, NBS) na hora de emitir a partir desta linha.</summary>
    public Guid ServicoId { get; set; }

    public decimal Quantidade { get; set; } = 1;

    public decimal ValorUnitario { get; set; }

    /// <summary>Nunca persistido — sempre Quantidade * ValorUnitario (ver ContratoServicoConfiguration.Ignore).</summary>
    public decimal ValorTotal => Quantidade * ValorUnitario;
}
