using System.Text.Json.Serialization;
using NfseSaaS.Application.Exceptions;

namespace NfseSaaS.Application.Fiscal.Retencoes;

/// <summary>
/// Percentuais de retenção federal. Nulo = o tributo não é retido.
/// Fica no Cliente (o tomador é quem retém) e pode ser ajustado em cada nota.
/// </summary>
public sealed record PercentuaisRetencao(
    decimal? Irrf = null,
    decimal? Pis = null,
    decimal? Cofins = null,
    decimal? Csll = null,
    decimal? Inss = null)
{
    public static readonly PercentuaisRetencao Nenhum = new();

    [JsonIgnore]
    public bool TemAlguma => Irrf is not null || Pis is not null || Cofins is not null || Csll is not null || Inss is not null;
}

/// <summary>Dados de uma nota para calcular descontos e retenções.</summary>
/// <param name="OpSimpNac">Regime da empresa (1 não optante, 2 MEI, 3 ME/EPP), como na DPS.</param>
/// <param name="AliquotaIss">Alíquota do ISS retido (%); só conta com <paramref name="IssRetido"/>.</param>
/// <param name="ReterAbaixoDoMinimo">true = não aplica a dispensa de R$ 10 (o tomador soma as notas do mês).</param>
public sealed record EntradaRetencoes(
    decimal ValorServico,
    decimal DescontoIncondicionado,
    decimal DescontoCondicionado,
    PercentuaisRetencao Percentuais,
    string OpSimpNac,
    bool IssRetido,
    decimal? AliquotaIss,
    bool ReterAbaixoDoMinimo = false);

/// <summary>Valores calculados (em R$, 2 casas) e os avisos para a tela.</summary>
/// <param name="BaseCalculo">Valor do serviço menos o desconto incondicionado.</param>
/// <param name="RetidoCsllTotal">PIS + COFINS + CSLL retidos: vai somado em vRetCSLL (NT 007).</param>
/// <param name="TpRetPisCofins">Código oficial do tipo de retenção (0, 3 a 9).</param>
/// <param name="Alertas">Situações que pedem confirmação do usuário (não bloqueiam).</param>
/// <param name="Dispensas">Retenções zeradas pela dispensa de valor mínimo.</param>
public sealed record ResultadoRetencoes(
    decimal BaseCalculo,
    decimal Irrf,
    decimal Pis,
    decimal Cofins,
    decimal Csll,
    decimal Inss,
    decimal IssRetido,
    decimal TotalRetencoes,
    decimal ValorLiquido,
    string TpRetPisCofins,
    IReadOnlyList<string> Alertas,
    IReadOnlyList<string> Dispensas)
{
    public decimal RetidoCsllTotal => Pis + Cofins + Csll;

    public bool TemRetencaoFederal => Irrf > 0 || Inss > 0 || RetidoCsllTotal > 0;
}

/// <summary>
/// Descontos e retenções federais da nota (C6), sem banco nem SEFIN.
/// - quem retém é o tomador: os percentuais padrão ficam no Cliente e
///   valem para a Nota Mensal e a automação; na tela, a nota pode mudar;
/// - base das retenções = valor do serviço − desconto incondicionado;
///   o condicionado não reduz a base, só o líquido;
/// - dispensa de valor mínimo: IRRF até R$ 10 (Lei 9.430/96, art. 67),
///   PIS+COFINS+CSLL até R$ 10 (Lei 10.833/03, art. 31) e INSS abaixo de
///   R$ 10: zera e avisa; o usuário pode reter mesmo assim;
/// - optante do Simples (MEI e ME/EPP) é dispensado do IRRF (IN RFB
///   765/2007) e de PIS/COFINS/CSLL (IN SRF 459/2004); INSS só no Anexo IV
///   com cessão de mão de obra; MEI não sofre retenção federal. Na
///   automação esses tributos não vão; na tela, o sistema avisa e o
///   usuário decide;
/// - NT 007: PIS, COFINS e CSLL retidos vão SOMADOS em vRetCSLL, com o
///   tipo em tpRetPisCofins (vPis/vCofins são débito próprio, não retenção).
/// </summary>
public static class RegrasRetencoesFederais
{
    public const decimal PadraoIrrf = 1.50m;
    public const decimal PadraoPis = 0.65m;
    public const decimal PadraoCofins = 3.00m;
    public const decimal PadraoCsll = 1.00m;
    public const decimal PadraoInss = 11.00m;

    /// <summary>Valor mínimo de retenção (R$): abaixo/até ele a retenção é dispensada.</summary>
    public const decimal ValorMinimo = 10.00m;

    public const string OpSimpNacNaoOptante = "1";
    public const string OpSimpNacMei = "2";
    public const string OpSimpNacMeEpp = "3";

    /// <summary>Frase opcional na descrição da nota do optante do Simples sem retenção federal.</summary>
    public const string FraseDispensaSimples =
        "Empresa optante pelo Simples Nacional: dispensada a retenção de IRRF (IN RFB 765/2007) e de PIS/COFINS/CSLL (IN SRF 459/2004).";

    public const int TamanhoMaximoDescricao = 2000;

    public static bool OptanteSimples(string? opSimpNac) => opSimpNac is OpSimpNacMei or OpSimpNacMeEpp;

    /// <summary>
    /// tpRetPisCofins pela combinação retida (tabela oficial, NT 007). Os
    /// códigos 1 e 2 (antigos, temporários) não são usados.
    /// </summary>
    public static string TpRetPisCofins(bool pis, bool cofins, bool csll) => (pis, cofins, csll) switch
    {
        (false, false, false) => "0",
        (true, true, true) => "3",
        (true, true, false) => "4",
        (true, false, false) => "5",
        (false, true, false) => "6",
        (false, true, true) => "7",
        (false, false, true) => "8",
        (true, false, true) => "9"
    };

    /// <summary>
    /// Padrão da automação (Nota Mensal, emissão sem tela): os percentuais do
    /// cliente, sem os tributos que o regime da empresa dispensa. Cliente
    /// compartilhado pode ser tomador de empresas de regimes diferentes.
    /// </summary>
    public static PercentuaisRetencao PadraoParaRegime(PercentuaisRetencao? doCliente, string? opSimpNac)
    {
        if (doCliente is null || !doCliente.TemAlguma)
            return PercentuaisRetencao.Nenhum;

        return opSimpNac switch
        {
            OpSimpNacMei => PercentuaisRetencao.Nenhum,
            OpSimpNacMeEpp => new PercentuaisRetencao(Inss: doCliente.Inss),
            _ => doCliente
        };
    }

    /// <summary>Avisos de regime para o que foi marcado (a tela pede confirmação).</summary>
    public static IReadOnlyList<string> AlertasRegime(PercentuaisRetencao percentuais, string? opSimpNac)
    {
        var alertas = new List<string>();
        if (opSimpNac == OpSimpNacMei)
        {
            if (percentuais.TemAlguma)
                alertas.Add("MEI não sofre retenção de tributos federais. Confira com o seu contador antes de emitir com retenção.");
            return alertas;
        }

        if (opSimpNac == OpSimpNacMeEpp)
        {
            if (percentuais.Irrf is not null)
                alertas.Add("Optante do Simples Nacional é dispensado da retenção de IRRF (IN RFB 765/2007).");
            if (percentuais.Pis is not null || percentuais.Cofins is not null || percentuais.Csll is not null)
                alertas.Add("Optante do Simples Nacional é dispensado da retenção de PIS/COFINS/CSLL (IN SRF 459/2004).");
            if (percentuais.Inss is not null)
                alertas.Add("No Simples, o INSS só é retido no Anexo IV com cessão de mão de obra ou empreitada.");
        }

        return alertas;
    }

    /// <summary>Confere os valores e percentuais informados. Erro = mensagem para a tela.</summary>
    public static void Validar(decimal valorServico, decimal descontoIncondicionado, decimal descontoCondicionado, PercentuaisRetencao percentuais)
    {
        if (descontoIncondicionado < 0 || descontoCondicionado < 0)
            throw new RegraNegocioException("Desconto não pode ser negativo.");
        if (descontoIncondicionado > 0 && descontoIncondicionado >= valorServico)
            throw new RegraNegocioException("O desconto incondicionado precisa ser menor que o valor do serviço.");
        if (descontoCondicionado > 0 && descontoIncondicionado + descontoCondicionado >= valorServico)
            throw new RegraNegocioException("Os descontos somados precisam ser menores que o valor do serviço.");
        if (decimal.Round(descontoIncondicionado, 2) != descontoIncondicionado || decimal.Round(descontoCondicionado, 2) != descontoCondicionado)
            throw new RegraNegocioException("Desconto tem no máximo 2 casas decimais.");

        ValidarPercentuais(percentuais);
    }

    /// <summary>Cada percentual informado: maior que 0, no máximo 100, até 2 casas.</summary>
    public static void ValidarPercentuais(PercentuaisRetencao percentuais)
    {
        foreach (var (nome, valor) in Itens(percentuais))
        {
            if (valor is not { } p) continue;
            if (p <= 0 || p > 100)
                throw new RegraNegocioException($"Percentual de {nome} deve ser maior que 0 e no máximo 100.");
            if (decimal.Round(p, 2) != p)
                throw new RegraNegocioException($"Percentual de {nome} tem no máximo 2 casas decimais.");
        }
    }

    /// <summary>Calcula descontos, retenções e líquido. Não valida (ver <see cref="Validar"/>).</summary>
    public static ResultadoRetencoes Calcular(EntradaRetencoes entrada)
    {
        var p = entrada.Percentuais;
        var baseCalculo = Math.Max(0, entrada.ValorServico - entrada.DescontoIncondicionado);

        var irrf = Aplicar(baseCalculo, p.Irrf);
        var pis = Aplicar(baseCalculo, p.Pis);
        var cofins = Aplicar(baseCalculo, p.Cofins);
        var csll = Aplicar(baseCalculo, p.Csll);
        var inss = Aplicar(baseCalculo, p.Inss);

        var dispensas = new List<string>();
        if (!entrada.ReterAbaixoDoMinimo)
        {
            if (irrf > 0 && irrf <= ValorMinimo)
            {
                dispensas.Add($"IRRF de {Moeda(irrf)} dispensado (até R$ 10,00).");
                irrf = 0;
            }

            var csrf = pis + cofins + csll;
            if (csrf > 0 && csrf <= ValorMinimo)
            {
                dispensas.Add($"PIS/COFINS/CSLL de {Moeda(csrf)} dispensados (até R$ 10,00).");
                pis = cofins = csll = 0;
            }

            if (inss > 0 && inss < ValorMinimo)
            {
                dispensas.Add($"INSS de {Moeda(inss)} dispensado (abaixo de R$ 10,00).");
                inss = 0;
            }
        }

        var issRetido = entrada.IssRetido && entrada.AliquotaIss is { } aliquota ? Arredondar(baseCalculo * aliquota / 100m) : 0m;
        var total = irrf + pis + cofins + csll + inss + issRetido;
        var liquido = entrada.ValorServico - entrada.DescontoIncondicionado - entrada.DescontoCondicionado - total;

        return new ResultadoRetencoes(
            BaseCalculo: baseCalculo,
            Irrf: irrf,
            Pis: pis,
            Cofins: cofins,
            Csll: csll,
            Inss: inss,
            IssRetido: issRetido,
            TotalRetencoes: total,
            ValorLiquido: liquido,
            TpRetPisCofins: TpRetPisCofins(pis > 0, cofins > 0, csll > 0),
            Alertas: AlertasRegime(p, entrada.OpSimpNac),
            Dispensas: dispensas);
    }

    /// <summary>Calcula e barra líquido zero ou negativo (a nota não pode ficar sem valor a receber).</summary>
    public static ResultadoRetencoes CalcularValidado(EntradaRetencoes entrada)
    {
        Validar(entrada.ValorServico, entrada.DescontoIncondicionado, entrada.DescontoCondicionado, entrada.Percentuais);
        var resultado = Calcular(entrada);
        if (resultado.ValorLiquido <= 0)
            throw new RegraNegocioException(
                $"Com estes descontos e retenções o valor líquido fica em {Moeda(resultado.ValorLiquido)}. Revise os percentuais.");
        return resultado;
    }

    /// <summary>
    /// Descrição com a frase de dispensa no fim, quando a empresa pediu, é
    /// optante e a nota não tem retenção federal. Não repete a frase e não
    /// passa do limite da DPS (sem espaço, a descrição vai como está).
    /// </summary>
    public static string DescricaoComFraseDispensa(string descricao, bool incluirFrase, string? opSimpNac, bool temRetencaoFederal)
    {
        if (!incluirFrase || !OptanteSimples(opSimpNac) || temRetencaoFederal)
            return descricao;
        if (descricao.Contains(FraseDispensaSimples, StringComparison.OrdinalIgnoreCase))
            return descricao;

        var texto = descricao.TrimEnd();
        var resultado = $"{texto}\n{FraseDispensaSimples}";
        return resultado.Length <= TamanhoMaximoDescricao ? resultado : descricao;
    }

    /// <summary>Percentual equivalente a um valor retido (para reabrir uma nota na tela).</summary>
    public static decimal? PercentualDe(decimal? valorRetido, decimal baseCalculo) =>
        valorRetido is > 0 && baseCalculo > 0 ? decimal.Round(valorRetido.Value * 100m / baseCalculo, 2, MidpointRounding.AwayFromZero) : null;

    private static decimal Aplicar(decimal baseCalculo, decimal? percentual) =>
        percentual is { } p ? Arredondar(baseCalculo * p / 100m) : 0m;

    private static decimal Arredondar(decimal valor) => decimal.Round(valor, 2, MidpointRounding.AwayFromZero);

    private static string Moeda(decimal valor) =>
        valor.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

    private static IEnumerable<(string Nome, decimal? Valor)> Itens(PercentuaisRetencao p)
    {
        yield return ("IRRF", p.Irrf);
        yield return ("PIS", p.Pis);
        yield return ("COFINS", p.Cofins);
        yield return ("CSLL", p.Csll);
        yield return ("INSS", p.Inss);
    }
}
