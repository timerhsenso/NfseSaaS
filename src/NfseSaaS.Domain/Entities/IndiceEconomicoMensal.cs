using NfseSaaS.Domain.Common;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Variação mensal de um índice econômico (IPCA, IGP-M, INPC...) — base do
/// cálculo do reajuste anual dos Contratos (acumulado dos 12 meses).
///
/// Entidade GLOBAL: NÃO implementa ITenantEntity — o IPCA de outubro é o
/// mesmo para todos os tenants (mesmo raciocínio de <see cref="CodigoNbs"/>).
/// Alimentada pela importação diária do Banco Central; o operador da
/// plataforma pode lançar/corrigir um mês à mão (Fonte=Manual), e esse
/// valor deixa de ser tocado pela importação.
/// </summary>
public sealed class IndiceEconomicoMensal : BaseEntity
{
    public IndiceReajusteContrato Indice { get; set; }

    /// <summary>Mês de referência — sempre o dia 1 (ex.: 2026-10-01 = outubro/2026).</summary>
    public DateOnly Competencia { get; set; }

    /// <summary>Variação do mês em PERCENTUAL (0,56 = 0,56%), como o Banco Central publica. Pode ser negativa (deflação).</summary>
    public decimal Variacao { get; set; }

    public FonteIndiceEconomico Fonte { get; set; } = FonteIndiceEconomico.BancoCentral;

    /// <summary>Quem lançou o valor manual (nulo quando veio do Banco Central).</summary>
    public Guid? AtualizadoPorUsuarioId { get; set; }

    /// <summary>Primeiro dia do mês da data informada.</summary>
    public static DateOnly NormalizarCompetencia(DateOnly data) => new(data.Year, data.Month, 1);
}
