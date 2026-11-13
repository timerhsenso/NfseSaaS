using NfseSaaS.Application.Fiscal.Retencoes;

namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>
/// C6: dados da tela de emissão para calcular descontos, retenções e
/// líquido. Mesmos campos e mesmo padrão do <see cref="EmitirNfseRequest"/>:
/// RetencoesFederais nulo = padrão do cliente para o regime da empresa.
/// </summary>
public sealed record SimularRetencoesRequest(
    Guid EmpresaId,
    Guid? ClienteId,
    decimal ValorServico,
    decimal? DescontoIncondicionado = null,
    decimal? DescontoCondicionado = null,
    PercentuaisRetencao? RetencoesFederais = null,
    bool IssRetido = false,
    decimal? AliquotaIss = null,
    bool ReterAbaixoDoMinimo = false);

/// <summary>
/// Resultado para a tela. <paramref name="Erro"/> preenchido = os valores
/// não fecham (ex.: líquido negativo); o resto vem zerado.
/// </summary>
/// <param name="Percentuais">Percentuais usados no cálculo (os informados ou o padrão do cliente).</param>
/// <param name="RetencaoFederalDisponivel">false = MEI: a tela esconde as retenções federais.</param>
/// <param name="FraseDispensa">Frase que vai no fim da descrição (empresa pediu, Simples e sem retenção federal).</param>
public sealed record SimulacaoRetencoesResponse(
    ResultadoRetencoes? Resultado,
    PercentuaisRetencao Percentuais,
    string OpSimpNac,
    bool RetencaoFederalDisponivel,
    string? FraseDispensa,
    string? Erro);

public interface ISimularRetencoesUseCase
{
    Task<SimulacaoRetencoesResponse> ExecutarAsync(SimularRetencoesRequest request, CancellationToken cancellationToken);
}
