using NfseSaaS.Domain.Common;

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

    /// <summary>Serviço do catálogo de onde vem a classificação fiscal (código de tributação nacional, NBS) na hora de emitir a partir deste Contrato.</summary>
    public Guid ServicoId { get; set; }

    /// <summary>Rótulo próprio do Contrato (ex.: "Manutenção sistema A") — pode ser igual ou diferente da descrição do Servico.</summary>
    public string Descricao { get; set; } = string.Empty;

    public decimal ValorAtual { get; set; }

    public DateOnly DataInicioContrato { get; set; }

    /// <summary>De quantos em quantos meses o valor deveria ser reajustado. Padrão de mercado: 12.</summary>
    public int PeriodicidadeReajusteMeses { get; set; } = 12;

    /// <summary>Índice de referência (ex.: "IPCA", "IGPM") — texto livre de propósito: nem sempre se sabe o índice na hora do cadastro.</summary>
    public string? IndiceReajuste { get; set; }

    /// <summary>Nulo até o primeiro reajuste ser registrado — nesse caso, o cálculo de vencimento usa DataInicioContrato como base.</summary>
    public DateOnly? DataUltimoReajuste { get; set; }

    /// <summary>Sobrescreve, só para este Contrato, Empresa.DiasAlertaReajusteContratoPadrao. Nulo = usa o padrão da Empresa.</summary>
    public int? DiasAlertaOverride { get; set; }

    public bool Ativo { get; set; } = true;
}
