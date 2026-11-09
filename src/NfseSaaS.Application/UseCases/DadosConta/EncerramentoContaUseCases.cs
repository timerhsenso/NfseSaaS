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
