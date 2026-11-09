using System.Globalization;
using FluentValidation;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.DadosConta;

/// <summary>
/// Regras do encerramento da conta (D2), sem banco nem e-mail:
/// - encerrar fecha o acesso na hora (como a suspensão) e cancela a assinatura;
/// - <see cref="DiasAteExpurgo"/> dias depois os dados são apagados
///   (Política 1.1, item 7: "demais dados, após o encerramento: até 90 dias");
///   sem nenhuma nota, a conta some inteira; com notas, ficam só os
///   documentos fiscais e o necessário para comprová-los (5 anos);
/// - até o expurgo o operador pode reabrir; depois, não.
/// </summary>
public static class RegrasEncerramentoConta
{
    public const int DiasAteExpurgo = 90;

    public const string MotivoSoAdministrador = "Só o administrador da conta pode encerrá-la.";
    public const string MotivoJaEncerrada = "Esta conta já está encerrada.";
    public const string MotivoContaDaPlataforma = "A conta da plataforma não pode ser encerrada.";
    public const string MotivoJaExpurgada = "Os dados desta conta já foram apagados: ela não pode mais ser reaberta.";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static DateOnly ExpurgoPrevisto(DateOnly diaDoEncerramento) => diaDoEncerramento.AddDays(DiasAteExpurgo);

    /// <summary>Hora de apagar os dados desta conta?</summary>
    public static bool DeveExpurgar(DateTimeOffset? encerradaEm, DateOnly? expurgoPrevistoEm, DateTimeOffset? expurgadaEm, DateOnly hoje) =>
        encerradaEm is not null && expurgadaEm is null && expurgoPrevistoEm is { } previsto && previsto <= hoje;

    /// <summary>Por que o administrador não pode encerrar agora (nulo = pode).</summary>
    public static string? MotivoParaNaoEncerrar(bool administrador, bool jaEncerrada, bool contaDaPlataforma)
    {
        if (!administrador)
            return MotivoSoAdministrador;
        if (contaDaPlataforma)
            return MotivoContaDaPlataforma;
        if (jaEncerrada)
            return MotivoJaEncerrada;
        return null;
    }

    /// <summary>Mensagem do login recusado para conta encerrada (a suspensa tem a sua).</summary>
    public static string MensagemLogin(DateTimeOffset encerradaEm, DateOnly? expurgoPrevistoEm, bool expurgada, string emailContato)
    {
        var quando = encerradaEm.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy", PtBr);
        if (expurgada || expurgoPrevistoEm is null)
            return $"Esta conta foi encerrada em {quando}. Dúvidas: {emailContato}.";
        return $"Esta conta foi encerrada em {quando}. Para reabrir ou pedir os seus dados até {expurgoPrevistoEm.Value.ToString("dd/MM/yyyy", PtBr)}, escreva para {emailContato}.";
    }

    public static string MotivoBloqueio(OrigemEncerramento origem, string? motivo) => origem switch
    {
        OrigemEncerramento.Cliente => "Conta encerrada a pedido do cliente.",
        OrigemEncerramento.Automatico => "Conta encerrada automaticamente (assinatura cancelada).",
        _ => "Conta encerrada pela plataforma" + (string.IsNullOrWhiteSpace(motivo) ? "." : ": " + motivo.Trim())
    };
}

/// <summary>Situação vista na tela "Meus dados" (card "Encerrar a conta").</summary>
public sealed record SituacaoEncerramentoResponse(
    bool PodeEncerrar,
    string? Motivo,
    int DiasAteExpurgo,
    bool TemAssinaturaAtiva,
    int Notas);

/// <summary>Para onde foi o código.</summary>
public sealed record SolicitacaoEncerramentoResponse(string EmailMascarado, int MinutosValidade);

public sealed record ConfirmarEncerramentoRequest(string Codigo);

public sealed class ConfirmarEncerramentoRequestValidator : AbstractValidator<ConfirmarEncerramentoRequest>
{
    public ConfirmarEncerramentoRequestValidator()
    {
        RuleFor(x => x.Codigo).NotEmpty().WithMessage("Informe o código recebido por e-mail.")
            .Matches(@"^\s*\d{6}\s*$").WithMessage("O código tem 6 números.");
    }
}

public sealed record EncerrarContaPlataformaRequest(string? Motivo);

/// <summary>Resultado do encerramento (para a tela dizer quando os dados somem).</summary>
public sealed record EncerramentoContaResponse(DateTimeOffset EncerradaEm, DateOnly ExpurgoPrevistoEm);

/// <summary>Encerrar a conta: pelo administrador (código por e-mail) ou pelo operador.</summary>
public interface IEncerrarContaUseCase
{
    Task<SituacaoEncerramentoResponse> ObterSituacaoAsync(CancellationToken cancellationToken);

    /// <summary>Administrador da conta atual: manda o código para o e-mail dele.</summary>
    Task<SolicitacaoEncerramentoResponse> SolicitarCodigoAsync(CancellationToken cancellationToken);

    /// <summary>Administrador da conta atual: confere o código e encerra.</summary>
    Task<EncerramentoContaResponse> ConfirmarAsync(ConfirmarEncerramentoRequest request, CancellationToken cancellationToken);

    /// <summary>Operador da plataforma (Plataforma › Contas).</summary>
    Task<EncerramentoContaResponse> EncerrarPelaPlataformaAsync(Guid tenantId, string? motivo, Guid operadorId, CancellationToken cancellationToken);
}

/// <summary>Resultado da passada diária do expurgo (para o log).</summary>
public sealed record ResultadoExpurgoContas(int Excluidas, int Anonimizadas, int ComErro);

/// <summary>Job diário: apaga os dados das contas encerradas cujo prazo venceu.</summary>
public interface IExpurgarContasEncerradasUseCase
{
    Task<ResultadoExpurgoContas> ExecutarAsync(DateOnly hoje, CancellationToken cancellationToken);
}

/// <summary>O que o encerramento automático faz hoje com uma conta cancelada.</summary>
public enum EtapaEncerramentoAutomatico
{
    Nada = 0,
    /// <summary>Primeiro aviso: "a conta será encerrada em DD/MM" (≥ 15 dias antes).</summary>
    AvisoAntecipado = 1,
    /// <summary>Último aviso, 3 dias antes.</summary>
    AvisoFinal = 2,
    Encerrar = 3
}

/// <summary>
/// Encerramento automático de conta com assinatura cancelada (D2, parte 2).
/// DESLIGADO por padrão (Encerramento:AutomaticoAposCancelarDias vazio): os
/// Termos 1.1 (8.5) prometem que, depois de cancelar, a conta continua
/// disponível. Só ligar com Termos novos em vigor.
/// - data prevista = último dia com acesso (ou o dia do cancelamento) + N dias;
/// - nunca encerra sem aviso: o primeiro aviso promete uma data pelo menos
///   <see cref="DiasAvisoAntecipado"/> dias à frente, e essa data fica gravada
///   na chave do e-mail (EmailLog.ChaveDeduplicacao), sem migration;
/// - aviso final <see cref="DiasAvisoFinal"/> dias antes; na data prometida, encerra
///   (e daí seguem os 90 dias até o expurgo, como no encerramento a pedido).
/// Assinou de novo → a assinatura deixa de estar Cancelada e nada acontece;
/// cancelou outra vez → novo ciclo (a chave leva o instante do cancelamento).
/// </summary>
public static class RegrasEncerramentoAutomatico
{
    public const int DiasAvisoAntecipado = 15;
    public const int DiasAvisoFinal = 3;

    /// <summary>Menor prazo aceito na configuração (um valor menor é elevado a este).</summary>
    public const int DiasMinimos = 30;

    private const string Prefixo = "encerramento-auto";

    public static DateOnly DataPrevista(DateTimeOffset canceladaEm, DateOnly? acessoAteAposCancelar, int dias)
    {
        var diaCancelamento = DateOnly.FromDateTime(canceladaEm.ToOffset(TimeSpan.FromHours(-3)).DateTime);
        var base_ = acessoAteAposCancelar is { } ate && ate > diaCancelamento ? ate : diaCancelamento;
        return base_.AddDays(Math.Max(dias, DiasMinimos));
    }

    /// <summary>Data a prometer no primeiro aviso: a prevista, mas nunca antes de 15 dias a partir de hoje.</summary>
    public static DateOnly DataPrometida(DateOnly prevista, DateOnly hoje) =>
        prevista > hoje.AddDays(DiasAvisoAntecipado) ? prevista : hoje.AddDays(DiasAvisoAntecipado);

    /// <param name="prometida">Data do primeiro aviso já enviado (nulo = nenhum aviso ainda).</param>
    public static EtapaEncerramentoAutomatico Etapa(DateOnly hoje, DateOnly prevista, DateOnly? prometida, bool avisoFinalEnviado)
    {
        if (prometida is not { } data)
            return hoje >= prevista.AddDays(-DiasAvisoAntecipado) ? EtapaEncerramentoAutomatico.AvisoAntecipado : EtapaEncerramentoAutomatico.Nada;
        if (hoje >= data)
            return EtapaEncerramentoAutomatico.Encerrar;
        if (!avisoFinalEnviado && hoje >= data.AddDays(-DiasAvisoFinal))
            return EtapaEncerramentoAutomatico.AvisoFinal;
        return EtapaEncerramentoAutomatico.Nada;
    }

    /// <summary>Início comum das chaves dos avisos deste ciclo de cancelamento.</summary>
    public static string ChavePrefixo(Guid tenantId, DateTimeOffset canceladaEm) =>
        $"{Prefixo}:{tenantId:N}:{canceladaEm.UtcTicks}:";

    public static string ChaveAviso(string prefixo, DateOnly prometida, EtapaEncerramentoAutomatico etapa) =>
        $"{prefixo}{prometida:yyyyMMdd}:{(etapa == EtapaEncerramentoAutomatico.AvisoFinal ? "final" : "antecipado")}";

    /// <summary>Data prometida gravada numa chave de aviso (nulo se a chave não for desse formato).</summary>
    public static DateOnly? DataNaChave(string chave)
    {
        var partes = chave.Split(':');
        return partes.Length == 5 && partes[0] == Prefixo
               && DateOnly.TryParseExact(partes[3], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
            ? d
            : null;
    }

    public static bool EhAvisoFinal(string chave) => chave.EndsWith(":final", StringComparison.Ordinal);
}

/// <param name="Desligado">A configuração está vazia: nada foi olhado.</param>
public sealed record ResultadoEncerramentoAutomatico(bool Desligado, int Avisadas, int Encerradas, int SemDestinatario, int ComErro);

/// <summary>Job diário do encerramento automático (D2, parte 2).</summary>
public interface IEncerrarContasCanceladasUseCase
{
    Task<ResultadoEncerramentoAutomatico> ExecutarAsync(DateOnly hoje, CancellationToken cancellationToken);
}
