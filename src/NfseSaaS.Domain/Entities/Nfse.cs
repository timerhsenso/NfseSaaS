using NfseSaaS.Domain.Common;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Representa uma NFS-e (e a DPS que a originou) dentro do SaaS.
/// Entidade TENANT-SCOPED: sofre o Global Query Filter por TenantId.
/// </summary>
public sealed class Nfse : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmpresaId { get; set; }

    public Guid ClienteId { get; set; }

    /// <summary>
    /// Contrato de onde a emissão puxou Servico/Descrição/Valor
    /// (opcional — nulo quando a nota foi emitida "avulsa", sem
    /// Contrato). Só rastreabilidade: os valores da nota já ficam
    /// gravados nos campos abaixo (snapshot), então mudar/excluir o
    /// Contrato depois nunca altera esta Nfse retroativamente.
    /// </summary>
    public Guid? ContratoId { get; set; }

    public int NumeroDps { get; set; }

    public string SerieDps { get; set; } = string.Empty;

    /// <summary>Número da NFS-e retornado pela SEFIN após autorização (nulo até então).</summary>
    public string? NumeroNfse { get; set; }

    /// <summary>Chave de acesso retornada pela SEFIN após autorização (nulo até então).</summary>
    public string? ChaveAcesso { get; set; }

    public DateOnly DataCompetencia { get; set; }

    public DateTimeOffset? DataEmissao { get; set; }

    public decimal ValorServico { get; set; }

    public string DescricaoServico { get; set; } = string.Empty;

    // --- Snapshot fiscal colunar: mesma lógica já aplicada a
    // DescricaoServico/ValorServico acima — o que foi de fato enviado na
    // DPS precisa sobreviver a uma mudança posterior no cadastro de
    // Servico ou no regime tributário da Empresa. Sem isto, o histórico
    // fiscal de uma nota já autorizada mudaria retroativamente se alguém
    // editasse o Servico ou o regime da Empresa depois — inaceitável para
    // auditoria fiscal. Copiados no instante da emissão, nunca depois. ---

    /// <summary>Código de tributação nacional (cTribNac) efetivamente usado nesta DPS — copiado de Servico na emissão.</summary>
    public string CodigoTributacaoNacional { get; set; } = string.Empty;

    /// <summary>Código NBS (cNBS) efetivamente usado nesta DPS — copiado de Servico na emissão.</summary>
    public string CodigoNbs { get; set; } = string.Empty;

    /// <summary>tribISSQN efetivamente usado nesta DPS — copiado do regime tributário da Empresa na emissão.</summary>
    public string TribIssqn { get; set; } = string.Empty;

    public string TpRetIssqn { get; set; } = string.Empty;

    public string CstPisCofins { get; set; } = string.Empty;

    public string TpRetPisCofins { get; set; } = string.Empty;

    public string PercentualTotalTributosSimplesNacional { get; set; } = string.Empty;

    /// <summary>
    /// Chave de idempotência opcional fornecida pelo chamador (ex.: retry
    /// automático de um client após timeout). Se já existir uma Nfse com a
    /// mesma (TenantId, EmpresaId, IdempotencyKey), a emissão NÃO é
    /// repetida — o resultado da tentativa original é retornado. Nula
    /// quando o chamador não informa (comportamento antigo, sem proteção
    /// contra duplicidade por retry).
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Valor líquido da NFS-e (tag vLiq, caminho NFSe/infNFSe/valores/vLiq
    /// no XML retornado pela SEFIN após autorização) — extraído do XML na
    /// emissão, nunca calculado localmente. É o único valor monetário do
    /// bloco "valores" que é simples, estável e sempre presente
    /// independente do regime tributário do emitente. O detalhamento
    /// tributário completo (retenções federais, base de cálculo do
    /// ISSQN, e os grupos de IBS/CBS da Reforma Tributária a partir de
    /// 2026) tem uma estrutura aninhada e ainda em evolução (NT 002/004)
    /// que não se presta a colunas fixas — fica reservado para o
    /// snapshot fiscal em jsonb (P1), não modelado aqui como colunas
    /// planas de ValorIss/ValorPis/etc.
    /// </summary>
    public decimal? ValorLiquido { get; set; }

    /// <summary>
    /// Snapshot fiscal (jsonb) do contexto de Empresa/Cliente/Servico no
    /// momento da emissão — ver NfseSaaS.Domain.Snapshots.NfseSnapshotFiscal
    /// pro formato exato. Complementa os campos colunares acima
    /// (CodigoTributacaoNacional, TribIssqn etc.): aqui fica o que não
    /// tem coluna própria (razão social, endereços, descrição do serviço
    /// no catálogo) mas que também precisa sobreviver a uma edição
    /// futura do cadastro sem alterar retroativamente o que esta nota
    /// representa. Serializado/desserializado pela camada Infrastructure
    /// (o Domain não depende de biblioteca de serialização).
    /// </summary>
    public string? SnapshotFiscalJson { get; set; }

    public NfseStatus Status { get; set; } = NfseStatus.Rascunho;

    /// <summary>XML da DPS gerada e assinada, enviada à SEFIN.</summary>
    public string? XmlDps { get; set; }

    /// <summary>XML da NFS-e retornado pela SEFIN após autorização.</summary>
    public string? XmlNfse { get; set; }

    /// <summary>Código do erro/rejeição retornado pela SEFIN, quando houver (ex.: "E0008").</summary>
    public string? CodigoErro { get; set; }

    public string? MensagemErro { get; set; }
}
