using System.Globalization;
using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Cobranca;

/// <summary>Tipos de aviso de cobrança enviados por e-mail aos administradores da conta.</summary>
public enum TipoAvisoCobranca
{
    /// <summary>Faltam 7 (ou 2) dias para o teste grátis em produção acabar.</summary>
    TesteAcabandoDias = 1,

    /// <summary>Faltam 2 notas (8 de 10 usadas) para o teste grátis acabar.</summary>
    TesteAcabandoNotas = 2,

    FaturaCriada = 3,
    FaturaVenceAmanha = 4,
    FaturaVencida = 5,

    /// <summary>Emissão em produção bloqueada (teste acabado ou atraso além da tolerância).</summary>
    EmissaoBloqueada = 6,

    AssinaturaCancelada = 7,
    PagamentoConfirmado = 8,

    /// <summary>Notas em produção no mês chegaram a 80% ou a 100% do plano (nunca bloqueia).</summary>
    LimiteNotas = 9,

    /// <summary>Mais empresas ativas do que o plano permite (nunca bloqueia).</summary>
    LimiteEmpresas = 10
}

/// <summary>Uso da conta frente ao plano assinado (nulo para quem não tem plano).</summary>
/// <param name="NotasPorMes">Nulo = plano sem limite de notas.</param>
/// <param name="NotasNoMes">Notas autorizadas em produção no mês corrente (Brasília).</param>
public sealed record UsoPlano(string PlanoNome, int MaxEmpresas, int? NotasPorMes, int EmpresasAtivas, int NotasNoMes);

/// <summary>
/// Foto da assinatura de uma conta, já com o que precisa ser contado no
/// banco (notas do teste). É tudo o que as regras de aviso olham.
/// </summary>
public sealed record EstadoAvisoCobranca(
    Guid TenantId,
    StatusAssinatura Status,
    DateTimeOffset? TesteIniciadoEm,
    int NotasProducaoNoTeste,
    string? AsaasAssinaturaId,
    DateOnly? AtrasadaDesde,
    string? UrlFaturaPendente,
    DateOnly? VencimentoFaturaPendente,
    decimal? ValorFaturaPendente,
    DateTimeOffset? UltimoPagamentoEm,
    DateTimeOffset? CanceladaEm,
    UsoPlano? Uso = null);

/// <summary>
/// Um aviso a mandar. <paramref name="Chave"/> vai para
/// EmailLog.ChaveDeduplicacao: achou a chave, o aviso já saiu.
/// <paramref name="Referencia"/> é a data que o texto cita (fim do teste,
/// vencimento da fatura, data do bloqueio...).
/// </summary>
public sealed record AvisoCobranca(TipoAvisoCobranca Tipo, string Chave, DateOnly? Referencia, int? Quantidade = null);

/// <summary>Nível visual da faixa do painel (cor do alerta).</summary>
public enum NivelFaixaCobranca
{
    Info = 1,
    Atencao = 2,
    Bloqueio = 3
}

/// <summary>Faixa no topo do painel. <paramref name="MostrarPagar"/>: há fatura em aberto para pagar.</summary>
/// <param name="SugerirTroca">Limite do plano: o botão leva a "Trocar de plano".</param>
public sealed record FaixaCobranca(NivelFaixaCobranca Nivel, string Mensagem, bool MostrarPagar, bool SugerirTroca = false);

/// <summary>
/// Quais avisos de cobrança uma conta deve receber AGORA e qual faixa o
/// painel mostra, sem banco nem e-mail (testável isolado). Quem chama já
/// filtrou: cobrança ligada para a conta (Cobranca:Ativa ou piloto) e conta
/// não isenta. Cada aviso tem uma chave estável, então rodar de novo (job
/// diário + disparo pelo webhook) nunca repete o mesmo e-mail.
///
/// Eventos (pagamento confirmado, cancelamento) só geram aviso dentro de
/// <see cref="JanelaEventoDias"/> dia: na prática saem segundos depois do
/// webhook; a janela só cobre o webhook perdido (o job diário manda) e
/// evita avisar pagamento antigo depois de um deploy (06/11: o pagamento de
/// teste da véspera foi avisado na troca de plano do dia seguinte).
/// </summary>
public static class RegrasAvisoCobranca
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Faixas do aviso de teste acabando, em dias restantes (a menor que couber vale).</summary>
    public static readonly int[] FaixasDiasTeste = { 2, 7 };

    /// <summary>Avisa quando sobram estas notas no teste (8 de 10 usadas).</summary>
    public const int NotasRestantesAviso = 2;

    public const int JanelaEventoDias = 1;

    /// <summary>Avisa quando as notas do mês chegam a este percentual do plano (e de novo aos 100%).</summary>
    public const int PercentualAvisoNotas = 80;

    public static IReadOnlyList<AvisoCobranca> Avaliar(EstadoAvisoCobranca estado, DateTimeOffset agora, DateOnly hoje)
    {
        var conta = estado.TenantId.ToString("N");
        var avisos = new List<AvisoCobranca>();

        // Pagamento confirmado (vale também para quem estava atrasado e pagou).
        if (estado.UltimoPagamentoEm is { } pagoEm && Recente(pagoEm, agora) && estado.Status == StatusAssinatura.Ativa)
            avisos.Add(new AvisoCobranca(TipoAvisoCobranca.PagamentoConfirmado,
                $"cob:pago:{conta}:{pagoEm.ToUniversalTime():yyyyMMddHHmmss}", DiaEmBrasilia(pagoEm)));

        // Cancelada: um aviso só (o texto já diz que a emissão em produção parou).
        if (estado.Status == StatusAssinatura.Cancelada)
        {
            if (estado.CanceladaEm is { } canceladaEm && Recente(canceladaEm, agora))
                avisos.Add(new AvisoCobranca(TipoAvisoCobranca.AssinaturaCancelada,
                    $"cob:cancelada:{conta}:{estado.AsaasAssinaturaId ?? canceladaEm.ToUniversalTime().ToString("yyyyMMddHHmmss")}",
                    DiaEmBrasilia(canceladaEm)));
            return avisos;
        }

        var situacao = RegrasCobranca.Avaliar(estado.Status, estado.TesteIniciadoEm, estado.NotasProducaoNoTeste,
            estado.AtrasadaDesde, agora, hoje);

        // Emissão em produção bloqueada.
        if (!situacao.PodeEmitirEmProducao)
        {
            if (estado.Status == StatusAssinatura.Atrasada && estado.AtrasadaDesde is { } desde)
                avisos.Add(new AvisoCobranca(TipoAvisoCobranca.EmissaoBloqueada,
                    $"cob:bloqueio:atraso:{conta}:{desde:yyyyMMdd}", desde.AddDays(Planos.ToleranciaAtrasoDias + 1)));
            else if (estado.TesteIniciadoEm is { } inicio)
                avisos.Add(new AvisoCobranca(TipoAvisoCobranca.EmissaoBloqueada,
                    $"cob:bloqueio:teste:{conta}:{inicio.ToUniversalTime():yyyyMMddHHmmss}", hoje));
        }

        // Fatura em aberto: vencida, vence amanhã ou recém-criada (só uma delas).
        if (VencimentoEmAtraso(estado, hoje) is { } vencidaEm)
        {
            if (hoje.DayNumber - vencidaEm.DayNumber <= Planos.ToleranciaAtrasoDias)
                avisos.Add(new AvisoCobranca(TipoAvisoCobranca.FaturaVencida,
                    $"cob:vencida:{conta}:{vencidaEm:yyyyMMdd}", vencidaEm));
        }
        else if (estado.UrlFaturaPendente is not null && estado.VencimentoFaturaPendente is { } vencimento)
        {
            if (vencimento == hoje.AddDays(1))
                avisos.Add(new AvisoCobranca(TipoAvisoCobranca.FaturaVenceAmanha,
                    $"cob:vence-amanha:{conta}:{vencimento:yyyyMMdd}", vencimento));
            else if (vencimento >= hoje)
                avisos.Add(new AvisoCobranca(TipoAvisoCobranca.FaturaCriada,
                    $"cob:fatura:{conta}:{vencimento:yyyyMMdd}", vencimento));
        }

        // Teste grátis acabando: só para quem ainda não assinou e ainda pode emitir.
        if (estado.Status == StatusAssinatura.Teste && estado.AsaasAssinaturaId is null
            && situacao.PodeEmitirEmProducao && estado.TesteIniciadoEm is { } testeInicio)
        {
            var termina = testeInicio.AddDays(Planos.TesteDias);
            var dias = DiasRestantes(termina, agora);
            var faixa = FaixasDiasTeste.Where(f => dias <= f).Select(f => (int?)f).FirstOrDefault();
            if (faixa is { } f)
                avisos.Add(new AvisoCobranca(TipoAvisoCobranca.TesteAcabandoDias,
                    $"cob:teste:{conta}:{testeInicio.ToUniversalTime():yyyyMMddHHmmss}:{f}d", DiaEmBrasilia(termina), dias));

            var notasRestantes = Planos.TesteNotas - estado.NotasProducaoNoTeste;
            if (notasRestantes <= NotasRestantesAviso)
                avisos.Add(new AvisoCobranca(TipoAvisoCobranca.TesteAcabandoNotas,
                    $"cob:teste-notas:{conta}:{testeInicio.ToUniversalTime():yyyyMMddHHmmss}", DiaEmBrasilia(termina), notasRestantes));
        }

        // Limite do plano (suave): um aviso por mês e por faixa, nunca bloqueia.
        if (estado.Uso is { } uso)
        {
            var mes = hoje.ToString("yyyyMM", CultureInfo.InvariantCulture);
            if (uso.EmpresasAtivas > uso.MaxEmpresas)
                avisos.Add(new AvisoCobranca(TipoAvisoCobranca.LimiteEmpresas,
                    $"cob:limite-empresas:{conta}:{mes}", hoje, uso.EmpresasAtivas));

            if (FaixaNotas(uso) is { } faixaNotas)
                avisos.Add(new AvisoCobranca(TipoAvisoCobranca.LimiteNotas,
                    $"cob:limite-notas:{conta}:{mes}:{faixaNotas}", hoje, uso.NotasNoMes));
        }

        return avisos;
    }

    /// <summary>100 quando as notas do mês chegaram ao limite, 80 quando passaram de 80%, nulo abaixo disso ou sem limite.</summary>
    public static int? FaixaNotas(UsoPlano uso)
    {
        if (uso.NotasPorMes is not { } limite || limite <= 0)
            return null;
        if (uso.NotasNoMes >= limite)
            return 100;
        return uso.NotasNoMes * 100 >= limite * PercentualAvisoNotas ? PercentualAvisoNotas : null;
    }

    /// <summary>
    /// Faixa do topo do painel (nula = nada a mostrar). Prioridade: bloqueio,
    /// cancelada, atraso, fatura em aberto, teste acabando.
    /// </summary>
    public static FaixaCobranca? Faixa(EstadoAvisoCobranca estado, DateTimeOffset agora, DateOnly hoje)
    {
        var temFatura = estado.UrlFaturaPendente is not null;

        if (estado.Status == StatusAssinatura.Cancelada)
            return estado.AsaasAssinaturaId is null
                ? null
                : new FaixaCobranca(NivelFaixaCobranca.Bloqueio,
                    "A assinatura desta conta foi cancelada: a emissão em produção está bloqueada. Assine de novo para voltar a emitir.", false);

        var situacao = RegrasCobranca.Avaliar(estado.Status, estado.TesteIniciadoEm, estado.NotasProducaoNoTeste,
            estado.AtrasadaDesde, agora, hoje);

        if (!situacao.PodeEmitirEmProducao)
            return new FaixaCobranca(NivelFaixaCobranca.Bloqueio, situacao.Bloqueio!, temFatura);

        if (VencimentoEmAtraso(estado, hoje) is { } vencidaEm)
        {
            // Atrasada: o aviso das regras já diz quando a emissão para.
            var mensagem = estado.Status == StatusAssinatura.Atrasada && situacao.Aviso is not null
                ? situacao.Aviso
                : $"A fatura{Valor(estado)} vencida em {Data(vencidaEm)} ainda não foi paga.";
            return new FaixaCobranca(NivelFaixaCobranca.Atencao, mensagem, temFatura);
        }

        if (temFatura)
        {
            var quando = estado.VencimentoFaturaPendente is { } v
                ? v == hoje ? " vence hoje" : v == hoje.AddDays(1) ? " vence amanhã" : $" vence em {Data(v)}"
                : " está em aberto";
            return new FaixaCobranca(NivelFaixaCobranca.Info, $"A fatura{Valor(estado)}{quando}.", true);
        }

        if (estado.Status == StatusAssinatura.Teste && estado.AsaasAssinaturaId is null && estado.TesteIniciadoEm is { } inicio)
        {
            var termina = inicio.AddDays(Planos.TesteDias);
            var dias = DiasRestantes(termina, agora);
            var notas = Planos.TesteNotas - estado.NotasProducaoNoTeste;
            if (dias <= FaixasDiasTeste.Max() || notas <= NotasRestantesAviso)
                return new FaixaCobranca(NivelFaixaCobranca.Info,
                    $"O teste grátis termina em {Data(DiaEmBrasilia(termina))} ou ao chegar a {Planos.TesteNotas} notas em produção (faltam {dias} dia(s) e {notas} nota(s)). Assine um plano para não parar de emitir.",
                    false);
        }

        if (estado.Uso is { } uso)
        {
            if (uso.EmpresasAtivas > uso.MaxEmpresas)
                return new FaixaCobranca(NivelFaixaCobranca.Atencao,
                    $"A conta tem {uso.EmpresasAtivas} empresas ativas e o plano {uso.PlanoNome} permite até {uso.MaxEmpresas}. Nada foi bloqueado, mas o plano certo para esse uso é um maior.",
                    false, SugerirTroca: true);

            if (FaixaNotas(uso) is { } faixaNotas)
                return new FaixaCobranca(faixaNotas == 100 ? NivelFaixaCobranca.Atencao : NivelFaixaCobranca.Info,
                    $"Este mês a conta já emitiu {uso.NotasNoMes} de {uso.NotasPorMes} notas do plano {uso.PlanoNome}. A emissão continua normalmente{(faixaNotas == 100 ? "; se o uso seguir acima do plano, troque para um maior" : "")}.",
                    false, SugerirTroca: true);
        }

        return null;
    }

    /// <summary>Vencimento da fatura em atraso (Atrasada, ou fatura em aberto com vencimento passado), ou nulo.</summary>
    private static DateOnly? VencimentoEmAtraso(EstadoAvisoCobranca estado, DateOnly hoje)
    {
        if (estado.Status == StatusAssinatura.Atrasada)
            return estado.AtrasadaDesde ?? estado.VencimentoFaturaPendente ?? hoje;

        return estado.UrlFaturaPendente is not null && estado.VencimentoFaturaPendente is { } v && v < hoje ? v : null;
    }

    private static int DiasRestantes(DateTimeOffset termina, DateTimeOffset agora) =>
        Math.Max(1, (int)Math.Ceiling((termina - agora).TotalDays));

    private static bool Recente(DateTimeOffset quando, DateTimeOffset agora) =>
        agora - quando <= TimeSpan.FromDays(JanelaEventoDias);

    private static DateOnly DiaEmBrasilia(DateTimeOffset quando) =>
        DateOnly.FromDateTime(quando.ToUniversalTime().AddHours(-3).DateTime);

    private static string Data(DateOnly data) => data.ToString("dd/MM/yyyy", PtBr);

    private static string Valor(EstadoAvisoCobranca estado) =>
        estado.ValorFaturaPendente is { } valor ? $" de {valor.ToString("C", PtBr)}" : string.Empty;
}
