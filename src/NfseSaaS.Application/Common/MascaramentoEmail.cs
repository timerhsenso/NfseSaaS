using System.Net;

namespace NfseSaaS.Application.Common;

/// <summary>
/// Esconde segredos (tokens de convite, redefinição de senha, confirmação
/// de e-mail) no corpo GRAVADO no EmailLog — a tela "E-mails enviados" não
/// pode mostrar um link que ainda funciona (quem vê a tela tomaria a conta
/// de outro usuário). O e-mail em si sai com o link verdadeiro, que só
/// existe na memória da fila. Cobre o token puro, codificado para URL e
/// codificado para HTML.
/// </summary>
public static class MascaramentoEmail
{
    public const string Oculto = "[oculto por segurança]";

    /// <summary>Mensagem para quem tenta reenviar (ou para o e-mail que não saiu antes de um reinício).</summary>
    public const string GereNovoLink =
        "Este e-mail tinha um link de acesso, que não fica guardado por segurança. Gere um novo: \"Reenviar convite\" em Usuários, \"Esqueci minha senha\" no login ou o reenvio da confirmação de e-mail.";

    public static string Mascarar(string corpoHtml, IEnumerable<string>? segredos)
    {
        if (segredos is null)
            return corpoHtml;

        var formas = segredos
            .Where(s => !string.IsNullOrEmpty(s))
            .SelectMany(s => new[]
            {
                s,
                Uri.EscapeDataString(s),
                WebUtility.HtmlEncode(s),
                WebUtility.HtmlEncode(Uri.EscapeDataString(s))
            })
            .Distinct()
            .OrderByDescending(s => s.Length); // a forma mais longa primeiro

        foreach (var forma in formas)
            corpoHtml = corpoHtml.Replace(forma, Oculto, StringComparison.Ordinal);

        return corpoHtml;
    }

    /// <summary>O corpo gravado teve segredo escondido (não dá para reenviar como está).</summary>
    public static bool TemSegredoOculto(string? corpoHtml) =>
        corpoHtml?.Contains(Oculto, StringComparison.Ordinal) == true;
}
