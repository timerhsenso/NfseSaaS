using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Catálogo oficial da Nomenclatura Brasileira de Serviços (NBS) — só os
/// códigos-FOLHA (9 dígitos, formato exigido no campo cNBS da DPS),
/// publicados pelo MDIC (gov.br/mdic, NBS versão 2.0). A tabela oficial
/// também tem linhas de agrupamento hierárquico com menos de 9 dígitos
/// (ex.: "1.01", "1.0101", "1.0101.1") — essas NÃO são valores válidos
/// pra submeter numa DPS, então não entram neste catálogo, só os
/// 920 códigos-folha.
///
/// Entidade GLOBAL: NÃO implementa ITenantEntity — mesmo raciocínio de
/// <see cref="CodigoTributacaoNacional"/>.
/// </summary>
public sealed class CodigoNbs : BaseEntity
{
    /// <summary>9 dígitos, sem pontuação (ex.: "101011100").</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Descricao { get; set; } = string.Empty;
}
