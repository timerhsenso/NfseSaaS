using NfseSaaS.Domain.Common;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Acordo comercial fixo entre uma Empresa e um Cliente — separa "quanto
/// esse cliente paga" (aqui) de "que tipo de serviço isso é perante o
/// fisco" (Servico, que só guarda classificação fiscal + um valor
/// padrão/sugerido, sem saber nada sobre clientes específicos).
///
/// Um Cliente pode ter vários Contratos (ex.: "Manutenção sistema A" e
/// "Manutenção sistema B", cada um com seu valor) — inclusive vários
/// apontando pro mesmo Servico. Nenhuma Nfse é obrigada a vir de um
/// Contrato: é uma forma OPCIONAL de pré-preencher a emissão pra quem
/// cobra o mesmo cliente pelo mesmo valor todo mês, sem afetar quem
/// prefere digitar Servico + valor na hora (fluxo que continua existindo
/// do jeito que já era).
///
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
/// </summary>
public sealed class Contrato : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmpresaId { get; set; }

    public Guid ClienteId { get; set; }

    /// <summary>Rótulo próprio do Contrato (ex.: "Manutenção sistema A") — pode ser igual ou diferente da descrição do Servico.</summary>
    public string Descricao { get; set; } = string.Empty;

    /// <summary>
    /// Total agregado — soma de ContratoServico.ValorTotal (linhas, Fase
    /// 6). Continua guardado aqui, em vez de sempre recalculado, pra não
    /// quebrar o fluxo de Reajuste (histórico em cima de um valor único)
    /// já testado em produção. Nunca editável fora do cadastro/Reajuste.
    /// </summary>
    public decimal ValorAtual { get; set; }

    public DateOnly DataInicioContrato { get; set; }

    public string? Observacao { get; set; }

    /// <summary>Status comercial do Contrato — Fase 6. Persistido, diferente de SituacaoContrato (calculada, só prazo de reajuste).</summary>
    public StatusContrato Status { get; set; } = StatusContrato.Ativo;

    /// <summary>Fim de vigência — nulo enquanto o Contrato não tiver prazo definido ou por tempo indeterminado.</summary>
    public DateOnly? DataFim { get; set; }

    /// <summary>Fase 6 — usado pela Fase 7 (Nota Mensal) pra selecionar automaticamente quais Contratos entram no lote do mês.</summary>
    public TipoCobrancaContrato TipoCobranca { get; set; } = TipoCobrancaContrato.Avulso;

    /// <summary>Fase 6 — se true, a tela de emissão em lote (Fase 7) permite ajustar o valor da linha na hora de gerar a nota; se false, usa sempre o valor do Contrato sem edição.</summary>
    public bool PermitirAlterarValorNaEmissao { get; set; } = true;

    /// <summary>De quantos em quantos meses o valor deveria ser reajustado. Padrão de mercado: 12.</summary>
    public int PeriodicidadeReajusteMeses { get; set; } = 12;

    /// <summary>Índice de referência (ex.: "IPCA", "IGPM") — texto livre de propósito: nem sempre se sabe o índice na hora do cadastro.</summary>
    public IndiceReajusteContrato? IndiceReajuste { get; set; }

    /// <summary>Nulo até o primeiro reajuste ser registrado — nesse caso, o cálculo de vencimento usa DataInicioContrato como base.</summary>
    public DateOnly? DataUltimoReajuste { get; set; }

    /// <summary>Sobrescreve, só para este Contrato, Empresa.DiasAlertaReajusteContratoPadrao. Nulo = usa o padrão da Empresa.</summary>
    public int? DiasAlertaOverride { get; set; }

    public bool Ativo { get; set; } = true;
}
