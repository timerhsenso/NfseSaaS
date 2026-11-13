using System.Globalization;
using NfseSaaS.Application.Fiscal.Retencoes;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.PainelFinanceiro;

/// <summary>Uma nota de produção autorizada, só o que o painel precisa.</summary>
public sealed record NotaPainel(DateOnly Competencia, Guid ClienteId, bool DeContrato, decimal Valor);

/// <summary>
/// Regras puras do painel financeiro (DB1). Faturamento = notas de
/// PRODUÇÃO, AUTORIZADAS, pela COMPETÊNCIA, pelo valor do serviço (bruto,
/// que é o que conta para o Simples). Cancelada e substituída ficam de fora
/// (a nota que substitui é autorizada e conta uma vez só).
/// </summary>
public static class RegrasPainelFinanceiro
{
    /// <summary>Meses no gráfico (o atual incluído).</summary>
    public const int MesesGrafico = 12;

    /// <summary>Maiores clientes mostrados.</summary>
    public const int MaioresClientes = 5;

    /// <summary>Itens de cada lista (notas a emitir e reajustes); os totais contam todos.</summary>
    public const int ItensPorLista = 8;

    /// <summary>
    /// Limite anual do MEI (LC 123/2006, art. 18-A §1º). Continua R$ 81.000
    /// em 2026; o aumento em discussão no Congresso, se aprovado, vale de
    /// 2027 em diante: é aqui que se muda.
    /// </summary>
    public static decimal LimiteMei(int ano) => 81_000m;

    /// <summary>Até 20% acima do limite o MEI sai só no ano seguinte; acima disso, retroativo (art. 18-A §7º, III).</summary>
    public const decimal ToleranciaMei = 0.20m;

    /// <summary>A partir de quanto do limite o painel pede atenção.</summary>
    public const decimal PercentualAtencao = 80m;

    /// <summary>Faixas da receita bruta em 12 meses do Simples (iguais em todos os anexos, LC 123 Anexos I a V).</summary>
    public static readonly decimal[] FaixasSimples = { 180_000m, 360_000m, 720_000m, 1_800_000m, 3_600_000m, 4_800_000m };

    /// <summary>Acima disto o ISS sai do DAS (sublimite).</summary>
    public const decimal SublimiteSimples = 3_600_000m;

    public static DateOnly InicioDoMes(DateOnly data) => new(data.Year, data.Month, 1);

    public static DateOnly FimDoMes(DateOnly data) => InicioDoMes(data).AddMonths(1).AddDays(-1);

    /// <summary>Primeiro mês que o painel lê: o do gráfico ou o 12º antes do atual (RBT12), o que vier antes.</summary>
    public static DateOnly InicioJanela(DateOnly hoje) => InicioDoMes(hoje).AddMonths(-12);

    /// <summary>Reajustes listados: até o fim do mês que vem (inclusive os atrasados).</summary>
    public static DateOnly ReajustesAte(DateOnly hoje) => FimDoMes(InicioDoMes(hoje).AddMonths(1));

    /// <summary>Perfil Contador olha uma empresa por vez; perfil Empresa vê a soma de todas.</summary>
    public static EscopoPainelFinanceiro EscopoPadrao(PerfilConta perfil) =>
        perfil == PerfilConta.Contador ? EscopoPainelFinanceiro.Empresa : EscopoPainelFinanceiro.Todas;

    /// <summary>Os 12 meses do gráfico (do mais antigo ao atual), sempre todos, mesmo sem nota.</summary>
    public static IReadOnlyList<FaturamentoMesResponse> PorMes(IEnumerable<NotaPainel> notas, DateOnly hoje)
    {
        var atual = InicioDoMes(hoje);
        var porMes = notas
            .GroupBy(n => InicioDoMes(n.Competencia))
            .ToDictionary(g => g.Key, g => g.ToList());

        return Enumerable.Range(0, MesesGrafico)
            .Select(i => atual.AddMonths(i - (MesesGrafico - 1)))
            .Select(mes => Mes(mes, porMes.TryGetValue(mes, out var doMes) ? doMes : null))
            .ToList();
    }

    public static FaturamentoMesResponse Mes(DateOnly competencia, IReadOnlyCollection<NotaPainel>? notas) =>
        notas is null || notas.Count == 0
            ? new FaturamentoMesResponse(competencia, 0m, 0m, 0)
            : new FaturamentoMesResponse(competencia,
                notas.Where(n => n.DeContrato).Sum(n => n.Valor),
                notas.Where(n => !n.DeContrato).Sum(n => n.Valor),
                notas.Count);

    /// <summary>RBT12: os 12 meses fechados antes do mês atual.</summary>
    public static decimal ReceitaUltimos12Meses(IEnumerable<NotaPainel> notas, DateOnly hoje)
    {
        var atual = InicioDoMes(hoje);
        var inicio = atual.AddMonths(-12);
        return notas.Where(n => n.Competencia >= inicio && n.Competencia < atual).Sum(n => n.Valor);
    }

    /// <summary>Maiores clientes nos 12 meses do gráfico, com a fatia de cada um no total.</summary>
    public static IReadOnlyList<ClienteFaturamentoResponse> Maiores(
        IEnumerable<NotaPainel> notasDaJanela, IReadOnlyDictionary<Guid, string> nomes, out decimal total)
    {
        var lista = notasDaJanela.ToList();
        total = lista.Sum(n => n.Valor);
        if (total <= 0)
            return Array.Empty<ClienteFaturamentoResponse>();

        var totalJanela = total;
        return lista
            .GroupBy(n => n.ClienteId)
            .Select(g => new { ClienteId = g.Key, Valor = g.Sum(n => n.Valor) })
            .OrderByDescending(x => x.Valor)
            .ThenBy(x => nomes.GetValueOrDefault(x.ClienteId), StringComparer.CurrentCulture)
            .Take(MaioresClientes)
            .Select(x => new ClienteFaturamentoResponse(
                x.ClienteId, nomes.GetValueOrDefault(x.ClienteId) ?? "Cliente", x.Valor, Percentual(x.Valor, totalJanela)))
            .ToList();
    }

    public static decimal Percentual(decimal parte, decimal total) =>
        total <= 0 ? 0m : Math.Round(parte * 100m / total, 1, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Limite do regime de UMA empresa: MEI pelo ano-calendário; ME/EPP pela
    /// faixa do Simples nos últimos 12 meses. Fora do Simples: nada.
    /// </summary>
    public static LimiteRegimeResponse? Limite(string? opSimpNac, decimal acumuladoAno, decimal ultimos12Meses, int ano)
    {
        if (opSimpNac == RegrasRetencoesFederais.OpSimpNacMei)
            return LimiteDoMei(acumuladoAno, ano);
        if (opSimpNac == RegrasRetencoesFederais.OpSimpNacMeEpp)
            return FaixaDoSimples(ultimos12Meses);
        return null;
    }

    public static LimiteRegimeResponse LimiteDoMei(decimal acumuladoAno, int ano)
    {
        var limite = LimiteMei(ano);
        var teto = limite * (1 + ToleranciaMei);
        var percentual = Percentual(acumuladoAno, limite);
        var titulo = $"Limite do MEI {ano}";

        if (acumuladoAno > teto)
            return new("mei", titulo,
                $"Passou de {Moeda(teto)} (limite + 20%): o desenquadramento do MEI é retroativo a janeiro. Fale com seu contador agora.",
                acumuladoAno, limite, percentual, "alerta");
        if (acumuladoAno > limite)
            return new("mei", titulo,
                $"Passou do limite de {Moeda(limite)}, mas ficou até 20% acima: a empresa deixa de ser MEI em janeiro. Converse com seu contador.",
                acumuladoAno, limite, percentual, "alerta");

        var falta = limite - acumuladoAno;
        return new("mei", titulo,
            $"Faltam {Moeda(falta)} para o limite de {Moeda(limite)} no ano. Notas emitidas fora do micoempresa não entram nesta conta.",
            acumuladoAno, limite, percentual, percentual >= PercentualAtencao ? "atencao" : "ok");
    }

    public static LimiteRegimeResponse FaixaDoSimples(decimal ultimos12Meses)
    {
        const string titulo = "Faixa do Simples";
        var ultimaFaixa = FaixasSimples[^1];
        if (ultimos12Meses > ultimaFaixa)
            return new("simples", titulo,
                $"Passou de {Moeda(ultimaFaixa)} nos últimos 12 meses, o teto do Simples Nacional. Fale com seu contador.",
                ultimos12Meses, ultimaFaixa, Percentual(ultimos12Meses, ultimaFaixa), "alerta");

        var indice = Array.FindIndex(FaixasSimples, f => ultimos12Meses <= f);
        var teto = FaixasSimples[indice];
        var percentual = Percentual(ultimos12Meses, teto);
        var texto = $"{indice + 1}ª faixa nos últimos 12 meses (até {Moeda(teto)}). " + (ultimos12Meses > SublimiteSimples
            ? "Acima de R$ 3,6 milhões o ISS sai do DAS: confira com seu contador."
            : "Conta só as notas emitidas aqui; a alíquota sobe a cada faixa.");
        var nivel = ultimos12Meses > SublimiteSimples ? "alerta" : percentual >= 90m ? "atencao" : "ok";
        return new("simples", titulo, texto, ultimos12Meses, teto, percentual, nivel);
    }

    public static string NomeIndice(IndiceReajusteContrato? indice) => indice switch
    {
        IndiceReajusteContrato.Ipca => "IPCA",
        IndiceReajusteContrato.Igpm => "IGP-M",
        IndiceReajusteContrato.Incc => "INCC",
        IndiceReajusteContrato.Inpc => "INPC",
        IndiceReajusteContrato.Selic => "Selic",
        IndiceReajusteContrato.Cdi => "CDI",
        IndiceReajusteContrato.Outro => "Outro",
        _ => "Sem índice"
    };

    /// <summary>Contrato que conta como receita no mês: começou até o fim dele e não terminou antes do início.</summary>
    public static bool VigenteNoMes(DateOnly inicioContrato, DateOnly? fimContrato, DateOnly mes) =>
        inicioContrato <= FimDoMes(mes) && (fimContrato is null || fimContrato.Value >= InicioDoMes(mes));

    private static string Moeda(decimal valor) =>
        valor.ToString("C0", CultureInfo.GetCultureInfo("pt-BR")).Replace(' ', ' ');
}
