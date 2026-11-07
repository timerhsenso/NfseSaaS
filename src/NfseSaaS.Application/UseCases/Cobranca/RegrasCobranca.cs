using System.Globalization;
using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Cobranca;

/// <summary>Se a conta pode emitir em produção agora e o que dizer a ela.</summary>
/// <param name="Bloqueio">Mensagem quando NÃO pode emitir em produção (nulo quando pode).</param>
/// <param name="Aviso">Lembrete quando pode, mas algo pede atenção (teste acabando, fatura vencida).</param>
public sealed record SituacaoCobranca(
    bool PodeEmitirEmProducao,
    string? Bloqueio,
    string? Aviso,
    DateTimeOffset? TesteTerminaEm);

/// <summary>
/// Regras da cobrança, sem banco nem gateway (testáveis isoladas):
/// - homologação nunca é bloqueada (quem chama só pergunta para produção);
/// - teste grátis em produção: Planos.TesteDias dias, sem limite de notas,
///   contados da primeira nota em produção (ou da assinatura feita antes);
/// - quem assinou durante o teste tem a primeira fatura vencendo no fim do
///   teste e, sem pagar, emite até esse vencimento mais a tolerância;
/// - fatura vencida: emite por mais Planos.ToleranciaAtrasoDias dias;
/// - cancelada: emite até o fim do período já pago ou do teste
///   (AcessoAteAposCancelar); depois, não emite em produção.
/// Consulta e download nunca são bloqueados (não passam por aqui).
/// </summary>
public static class RegrasCobranca
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public const string ComplementoBloqueio =
        " As notas em homologação, as consultas e os downloads continuam liberados.";

    public const string MensagemSemTesteGratis =
        "O CNPJ desta empresa já usou o teste grátis em outra conta, por isso esta conta não tem teste: assine um plano em Assinatura para emitir em produção. Se achar que é engano, fale com o suporte.";

    /// <param name="primeiraCobrancaEm">Vencimento da primeira fatura de quem assinou durante o teste (nulo sem assinatura).</param>
    /// <param name="acessoAteAposCancelar">Cancelada pela tela: último dia com emissão em produção.</param>
    /// <param name="semTesteGratis">Um CNPJ da conta já usou o teste em outra conta: o teste "acaba" ao começar.</param>
    public static SituacaoCobranca Avaliar(
        StatusAssinatura status,
        DateTimeOffset? testeIniciadoEm,
        DateOnly? primeiraCobrancaEm,
        DateOnly? atrasadaDesde,
        DateOnly? acessoAteAposCancelar,
        DateTimeOffset agora,
        DateOnly hoje,
        bool semTesteGratis = false)
    {
        switch (status)
        {
            case StatusAssinatura.Ativa:
                return new SituacaoCobranca(true, null, null, null);

            case StatusAssinatura.Cancelada:
                if (acessoAteAposCancelar is { } ate && hoje <= ate)
                    return new SituacaoCobranca(true, null,
                        $"A assinatura foi cancelada. A emissão em produção continua até {Data(ate)}; depois disso, só com uma nova assinatura.",
                        null);
                return new SituacaoCobranca(false,
                    "A assinatura desta conta foi cancelada: a emissão em produção está bloqueada. Assine de novo em Assinatura para voltar a emitir." + ComplementoBloqueio,
                    null, null);

            case StatusAssinatura.Atrasada:
                return FaturaEmAtraso(atrasadaDesde ?? hoje, hoje, primeira: false, testeTerminaEm: null);

            default: // Teste
            {
                if (testeIniciadoEm is null)
                    return new SituacaoCobranca(true, null, null, null);

                var termina = semTesteGratis ? testeIniciadoEm.Value : testeIniciadoEm.Value.AddDays(Planos.TesteDias);
                if (agora < termina)
                {
                    var dias = Math.Max(1, (int)Math.Ceiling((termina - agora).TotalDays));
                    var aviso = primeiraCobrancaEm is { } venc
                        ? $"Teste grátis: faltam {dias} dia(s). A primeira fatura da assinatura vence em {Data(venc)}."
                        : $"Teste grátis: faltam {dias} dia(s) para emitir em produção sem assinatura.";
                    return new SituacaoCobranca(true, null, aviso, termina);
                }

                // Assinou durante o teste: a primeira fatura decide (com tolerância).
                if (primeiraCobrancaEm is { } vencimento)
                {
                    if (hoje <= vencimento)
                        return new SituacaoCobranca(true, null,
                            $"O teste grátis terminou. A primeira fatura da assinatura vence em {Data(vencimento)}.",
                            termina);
                    return FaturaEmAtraso(vencimento, hoje, primeira: true, testeTerminaEm: termina);
                }

                return new SituacaoCobranca(false,
                    (semTesteGratis
                        ? MensagemSemTesteGratis
                        : $"O teste grátis de {Planos.TesteDias} dias terminou. Assine um plano em Assinatura para continuar emitindo em produção.")
                    + ComplementoBloqueio,
                    null, termina);
            }
        }
    }

    /// <summary>
    /// Vencimento da primeira fatura se a conta assinar agora: no fim do teste
    /// (quem ainda não começou o teste começa agora, ao assinar); depois do
    /// teste, hoje; quem cancelou e ainda tem dias pagos, no dia seguinte a eles.
    /// </summary>
    /// <returns>O vencimento e se a assinatura deve iniciar o teste agora.</returns>
    public static (DateOnly Vencimento, bool IniciarTeste) PrimeiroVencimento(
        StatusAssinatura status,
        DateTimeOffset? testeIniciadoEm,
        DateOnly? acessoAteAposCancelar,
        DateTimeOffset agora,
        DateOnly hoje,
        bool semTesteGratis = false)
    {
        if (status == StatusAssinatura.Cancelada)
            return (acessoAteAposCancelar is { } ate && ate >= hoje ? ate.AddDays(1) : hoje, false);

        if (status != StatusAssinatura.Teste)
            return (hoje, false);

        // Sem teste (CNPJ já usou em outra conta): vence hoje; o registro do
        // início do teste fica com quem chama.
        if (semTesteGratis)
            return (hoje, testeIniciadoEm is null);

        if (testeIniciadoEm is null)
            return (Maior(DiaEmBrasilia(agora.AddDays(Planos.TesteDias)), hoje), true);

        var termina = testeIniciadoEm.Value.AddDays(Planos.TesteDias);
        return (agora < termina ? Maior(DiaEmBrasilia(termina), hoje) : hoje, false);
    }

    private static SituacaoCobranca FaturaEmAtraso(DateOnly vencimento, DateOnly hoje, bool primeira, DateTimeOffset? testeTerminaEm)
    {
        var fatura = primeira ? "A primeira fatura da assinatura" : "A fatura";
        var limite = vencimento.AddDays(Planos.ToleranciaAtrasoDias);
        if (hoje > limite)
            return new SituacaoCobranca(false,
                $"{fatura}, vencida em {Data(vencimento)}, não foi paga: a emissão em produção está bloqueada. Pague a fatura em Assinatura; a liberação é automática assim que o pagamento for confirmado." + ComplementoBloqueio,
                null, testeTerminaEm);

        return new SituacaoCobranca(true, null,
            $"{fatura} vencida em {Data(vencimento)} ainda não foi paga. A emissão em produção para em {Data(limite.AddDays(1))} se ela não for paga.",
            testeTerminaEm);
    }

    private static DateOnly Maior(DateOnly a, DateOnly b) => a > b ? a : b;

    private static string Data(DateOnly data) => data.ToString("dd/MM/yyyy", PtBr);

    internal static DateOnly DiaEmBrasilia(DateTimeOffset quando) =>
        DateOnly.FromDateTime(quando.ToUniversalTime().AddHours(-3).DateTime);
}
