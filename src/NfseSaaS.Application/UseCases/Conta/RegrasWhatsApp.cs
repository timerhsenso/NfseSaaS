using NfseSaaS.Application.Exceptions;

namespace NfseSaaS.Application.UseCases.Conta;

/// <summary>
/// WhatsApp do dono da conta: celular do Brasil (DDD válido + 9 dígitos
/// começando por 9), guardado no formato internacional sem "+"
/// ("5571999998888"), que é o que os links wa.me e as APIs de envio usam.
/// </summary>
public static class RegrasWhatsApp
{
    public const int TamanhoGuardado = 13;

    // DDDs em uso no Brasil (Anatel).
    private static readonly HashSet<int> Ddds = new()
    {
        11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 24, 27, 28, 31, 32, 33, 34, 35, 37, 38,
        41, 42, 43, 44, 45, 46, 47, 48, 49, 51, 53, 54, 55, 61, 62, 63, 64, 65, 66, 67, 68, 69,
        71, 73, 74, 75, 77, 79, 81, 82, 83, 84, 85, 86, 87, 88, 89, 91, 92, 93, 94, 95, 96, 97, 98, 99
    };

    public const string MensagemInvalido = "Informe o WhatsApp com DDD, ex.: (71) 99999-8888 (celular do Brasil).";

    /// <summary>
    /// Aceita com ou sem máscara, com ou sem 55/+55 e zero de operadora na
    /// frente do DDD. Vazio = null (sem WhatsApp); fora do formato = null
    /// também — use <see cref="Validar"/> quando o erro deve aparecer.
    /// </summary>
    public static string? Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        var digitos = new string(texto.Where(char.IsAsciiDigit).ToArray());
        if (digitos.Length == 13 && digitos.StartsWith("55"))
            digitos = digitos[2..];
        else if (digitos.Length == 12 && digitos.StartsWith('0'))
            digitos = digitos[1..];

        if (digitos.Length != 11 || digitos[2] != '9' || !Ddds.Contains(int.Parse(digitos[..2])))
            return null;

        return "55" + digitos;
    }

    /// <summary>Vazio = sem WhatsApp (null); preenchido e inválido = erro com mensagem para a tela.</summary>
    public static string? Validar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;
        return Normalizar(texto) ?? throw new RegraNegocioException(MensagemInvalido);
    }

    /// <summary>"5571999998888" → "(71) 99999-8888".</summary>
    public static string? Formatar(string? guardado) =>
        guardado is { Length: TamanhoGuardado }
            ? $"({guardado[2..4]}) {guardado[4..9]}-{guardado[9..]}"
            : null;

    /// <summary>Link para abrir a conversa (wa.me), com a primeira mensagem já escrita.</summary>
    public static string? LinkConversa(string? guardado, string? mensagem = null) =>
        guardado is { Length: TamanhoGuardado }
            ? $"https://wa.me/{guardado}" + (string.IsNullOrWhiteSpace(mensagem) ? "" : "?text=" + Uri.EscapeDataString(mensagem))
            : null;
}
