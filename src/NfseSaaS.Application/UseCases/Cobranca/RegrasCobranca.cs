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
/// - teste grátis em produção: Planos.TesteDias dias OU Planos.TesteNotas
///   notas autorizadas, o que acabar primeiro, contados da primeira nota em
///   produção;
/// - fatura vencida: emite por mais Planos.ToleranciaAtrasoDias dias;
/// - cancelada: não emite em produção.
/// Consulta e download nunca são bloqueados (não passam por aqui).
/// </summary>
public static class RegrasCobranca
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public const string ComplementoBloqueio =
        " As notas em homologação, as consultas e os downloads continuam liberados.";

    public static SituacaoCobranca Avaliar(
        StatusAssinatura status,
        DateTimeOffset? testeIniciadoEm,
        int notasProducaoNoTeste,
        DateOnly? atrasadaDesde,
        DateTimeOffset agora,
        DateOnly hoje)
    {
        switch (status)
        {
            case StatusAssinatura.Ativa:
                return new SituacaoCobranca(true, null, null, null);

            case StatusAssinatura.Cancelada:
                return new SituacaoCobranca(false,
                    "A assinatura desta conta foi cancelada: a emissão em produção está bloqueada. Assine de novo em Assinatura para voltar a emitir." + ComplementoBloqueio,
                    null, null);

            case StatusAssinatura.Atrasada:
            {
                var vencimento = atrasadaDesde ?? hoje;
                var limite = vencimento.AddDays(Planos.ToleranciaAtrasoDias);
                if (hoje > limite)
                    return new SituacaoCobranca(false,
                        $"A fatura vencida em {vencimento.ToString("dd/MM/yyyy", PtBr)} não foi paga: a emissão em produção está bloqueada. Pague a fatura em Assinatura; a liberação é automática assim que o pagamento for confirmado." + ComplementoBloqueio,
                        null, null);

                return new SituacaoCobranca(true, null,
                    $"A fatura vencida em {vencimento.ToString("dd/MM/yyyy", PtBr)} ainda não foi paga. A emissão em produção para em {limite.AddDays(1).ToString("dd/MM/yyyy", PtBr)} se ela não for paga.",
                    null);
            }

            default: // Teste
            {
                if (testeIniciadoEm is null)
                    return new SituacaoCobranca(true, null, null, null);

                var termina = testeIniciadoEm.Value.AddDays(Planos.TesteDias);
                if (agora >= termina || notasProducaoNoTeste >= Planos.TesteNotas)
                    return new SituacaoCobranca(false,
                        $"O teste grátis terminou ({Planos.TesteDias} dias ou {Planos.TesteNotas} notas em produção, o que acabar primeiro). Assine um plano em Assinatura para continuar emitindo em produção." + ComplementoBloqueio,
                        null, termina);

                var dias = Math.Max(1, (int)Math.Ceiling((termina - agora).TotalDays));
                var notas = Planos.TesteNotas - notasProducaoNoTeste;
                return new SituacaoCobranca(true, null,
                    $"Teste grátis: faltam {dias} dia(s) e {notas} nota(s) em produção.",
                    termina);
            }
        }
    }
}
