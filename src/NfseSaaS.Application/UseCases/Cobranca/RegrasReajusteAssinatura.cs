using System.Globalization;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Cobranca;

/// <summary>Como está a assinatura na hora de agendar ou aplicar um reajuste.</summary>
/// <param name="TemAssinaturaNoGateway">Há assinatura criada no Asaas (AsaasAssinaturaId).</param>
/// <param name="CobrancaValeParaConta">Cobrança ligada (ou piloto) e conta não isenta.</param>
/// <param name="AssinadaEm">Dia da assinatura (Brasília).</param>
/// <param name="UltimoReajusteEm">AplicarEm do último reajuste aplicado (nulo = nunca).</param>
public sealed record SituacaoReajuste(
    StatusAssinatura Status,
    bool TemAssinaturaNoGateway,
    bool CobrancaValeParaConta,
    int? Plano,
    decimal? PrecoMensal,
    bool Fundador,
    DateOnly? AssinadaEm,
    DateOnly? UltimoReajusteEm,
    bool TemReajusteAgendado);

/// <summary>
/// Regras puras do reajuste de quem já assina (B2), na letra dos Termos 1.1:
/// item 8.5 (preço de tabela, no máximo uma vez por ano, aviso de pelo menos
/// 30 dias) e item 8.3 (preço de fundador fica fora).
/// "Uma vez por ano" é contado por assinatura: 12 meses desde que assinou ou
/// desde o último reajuste aplicado, o que for mais recente.
/// </summary>
public static class RegrasReajusteAssinatura
{
    public const int AvisoMinimoDias = 30;
    public const int IntervaloMinimoMeses = 12;

    /// <summary>Mais longe que isso é quase sempre engano de data.</summary>
    public const int AgendamentoMaximoDias = 180;

    /// <summary>Recusas seguidas do Asaas até virar Erro (o operador olha).</summary>
    public const int MaxTentativas = 5;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static DateOnly DataMinima(DateOnly hoje) => hoje.AddDays(AvisoMinimoDias);

    /// <summary>Nulo = data aceita.</summary>
    public static string? ProblemaNaData(DateOnly aplicarEm, DateOnly hoje)
    {
        if (aplicarEm < DataMinima(hoje))
            return $"O reajuste precisa de aviso de pelo menos {AvisoMinimoDias} dias: escolha uma data a partir de {Dia(DataMinima(hoje))}.";
        if (aplicarEm > hoje.AddDays(AgendamentoMaximoDias))
            return $"Escolha uma data até {Dia(hoje.AddDays(AgendamentoMaximoDias))} ({AgendamentoMaximoDias} dias).";
        return null;
    }

    /// <summary>
    /// Por que a assinatura fica de fora de um reajuste para <paramref name="precoNovo"/>
    /// em <paramref name="aplicarEm"/>. Nulo = entra.
    /// </summary>
    public static string? MotivoForaDoReajuste(SituacaoReajuste s, int plano, decimal precoNovo, DateOnly aplicarEm)
    {
        if (s.Status == StatusAssinatura.Cancelada || !s.TemAssinaturaNoGateway)
            return "Sem assinatura ativa.";
        if (s.Plano != plano)
            return "Está em outro plano.";
        if (!s.CobrancaValeParaConta)
            return "Cobrança desligada ou conta isenta.";
        if (s.Fundador)
            return "Preço de fundador: fica fora do reajuste.";
        if (s.TemReajusteAgendado)
            return "Já tem um reajuste agendado.";
        if (s.PrecoMensal is not { } atual || atual >= precoNovo)
            return $"Já paga {Reais(s.PrecoMensal)}: igual ou acima do novo preço.";

        var desde = MaisRecente(s.AssinadaEm, s.UltimoReajusteEm);
        if (desde is { } d && d.AddMonths(IntervaloMinimoMeses) > aplicarEm)
        {
            var origem = s.UltimoReajusteEm == desde ? "o último reajuste" : "a assinatura";
            return $"Menos de {IntervaloMinimoMeses} meses desde {origem} ({Dia(d)}): pode a partir de {Dia(d.AddMonths(IntervaloMinimoMeses))}.";
        }

        return null;
    }

    /// <summary>
    /// Na data, a assinatura ainda é a que foi avisada? Nulo = aplica.
    /// Qualquer mudança (troca de plano, cancelamento, valor diferente) vence
    /// o reajuste: ele é cancelado, nunca aplicado por cima.
    /// </summary>
    public static string? MotivoNaoAplicar(SituacaoReajuste s, int plano, decimal precoAnterior)
    {
        if (s.Status == StatusAssinatura.Cancelada || !s.TemAssinaturaNoGateway)
            return "A assinatura foi cancelada antes da data.";
        if (s.Plano != plano)
            return "A conta trocou de plano antes da data.";
        if (s.Fundador)
            return "A assinatura passou a ter preço de fundador.";
        if (s.PrecoMensal != precoAnterior)
            return "O valor da assinatura mudou antes da data.";
        if (!s.CobrancaValeParaConta)
            return "Cobrança desligada ou conta isenta na data.";
        return null;
    }

    public static string ChaveAviso(Guid reajusteId) => $"reajuste-assinatura:{reajusteId:N}";

    public static string Dia(DateOnly dia) => dia.ToString("dd/MM/yyyy", PtBr);

    public static string Reais(decimal? valor) => valor is { } v ? v.ToString("C", PtBr) : "—";

    private static DateOnly? MaisRecente(DateOnly? a, DateOnly? b) =>
        a is null ? b : b is null ? a : (a > b ? a : b);
}
