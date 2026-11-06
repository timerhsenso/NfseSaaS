using System.Text.RegularExpressions;

namespace NfseSaaS.Application.UseCases.Indicacao;

/// <summary>
/// Regras do convite por e-mail da tela "Indique e ganhe" (anti-spam):
/// até <see cref="LimitePorDia"/> convites por conta por dia (Brasília),
/// até <see cref="MaximoPorEnvio"/> endereços por envio, e o mesmo
/// endereço recebe no máximo 1 convite a cada <see cref="IntervaloMesmoEmailDias"/>
/// dias, de qualquer conta. Endereço que já tem usuário não recebe.
/// O texto do convite é fixo (sem mensagem livre), para o canal não virar
/// envio de conteúdo arbitrário pelo domínio da plataforma.
/// </summary>
public static partial class RegrasConviteIndicacao
{
    public const int LimitePorDia = 10;
    public const int MaximoPorEnvio = 10;
    public const int IntervaloMesmoEmailDias = 30;
    public const int TamanhoMaximoEmail = 254;

    /// <summary>Origem gravada no cadastro de quem chegou pelo convite (?origem=).</summary>
    public const string Origem = "convite-email";

    public const string MotivoInvalido = "E-mail inválido.";
    public const string MotivoProprio = "É o seu próprio e-mail.";
    public const string MotivoBloqueado = "Este endereço já usa o micoempresa ou já recebeu um convite nos últimos 30 dias.";
    public const string MotivoLimite = "Limite de convites de hoje atingido.";

    /// <summary>
    /// Separa a lista digitada (vírgula, ponto e vírgula, espaço ou quebra
    /// de linha) em endereços válidos — normalizados e sem repetição, na
    /// ordem digitada — e inválidos.
    /// </summary>
    public static (IReadOnlyList<string> Validos, IReadOnlyList<string> Invalidos) Separar(string? texto)
    {
        var validos = new List<string>();
        var invalidos = new List<string>();
        foreach (var parte in RegexSeparador().Split(texto ?? string.Empty))
        {
            var email = parte.Trim().Trim('<', '>').ToLowerInvariant();
            if (email.Length == 0)
                continue;
            if (EmailValido(email))
            {
                if (!validos.Contains(email))
                    validos.Add(email);
            }
            else if (!invalidos.Contains(email))
            {
                invalidos.Add(email);
            }
        }
        return (validos, invalidos);
    }

    public static bool EmailValido(string email) =>
        email.Length <= TamanhoMaximoEmail && RegexEmail().IsMatch(email);

    /// <summary>Link do convite: o link de indicação da conta com a origem do convite.</summary>
    public static string Link(string linkIndicacao) =>
        linkIndicacao + (linkIndicacao.Contains('?') ? "&" : "?") + "origem=" + Origem;

    [GeneratedRegex(@"[\s,;]+")]
    private static partial Regex RegexSeparador();

    [GeneratedRegex(@"^[a-z0-9._%+\-]+@[a-z0-9\-]+(\.[a-z0-9\-]+)*\.[a-z]{2,}$")]
    private static partial Regex RegexEmail();
}

/// <param name="Emails">Um ou mais e-mails separados por vírgula, ponto e vírgula, espaço ou linha.</param>
public sealed record ConvidarPorEmailRequest(string Emails);

/// <param name="Motivo">Por que não foi enviado (nulo quando enviado).</param>
public sealed record ResultadoConvite(string Email, bool Enviado, string? Motivo);

public sealed record ConvidarPorEmailResponse(IReadOnlyList<ResultadoConvite> Resultados, int RestamHoje);

/// <summary>Convite enviado pela conta (lista da tela).</summary>
public sealed record ConviteEnviado(string Email, DateTimeOffset EnviadoEm);

/// <summary>Envia convites por e-mail com o link de indicação da conta atual.</summary>
public interface IConvidarPorEmailUseCase
{
    /// <param name="linkIndicacao">Link de indicação da conta (…/?ref=CODIGO).</param>
    Task<ConvidarPorEmailResponse> ExecutarAsync(ConvidarPorEmailRequest request, string linkIndicacao, CancellationToken cancellationToken);
}
