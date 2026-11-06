namespace NfseSaaS.Application.Common;

/// <summary>
/// Contato do suporte mostrado no sistema (rodapés, ajuda, página de erro,
/// mensagens de erro que mandam "falar com o suporte" e e-mails de
/// cobrança). Seção "Suporte" do appsettings; na VPS, Suporte__Email,
/// Suporte__WhatsApp e Suporte__Horario no docker-compose.prod.yml.
/// Campo vazio = não aparece.
/// </summary>
public sealed class SuporteOptions
{
    public const string SectionName = "Suporte";

    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Número com DDI e DDD, só dígitos ou com máscara (ex.: "5571999998888"
    /// ou "+55 (71) 99999-8888"). Vazio = sem WhatsApp.
    /// </summary>
    public string WhatsApp { get; set; } = string.Empty;

    /// <summary>Texto livre (ex.: "segunda a sexta, das 9h às 18h"). Vazio = não mostra.</summary>
    public string Horario { get; set; } = string.Empty;

    public bool TemEmail => !string.IsNullOrWhiteSpace(Email);

    public bool TemWhatsApp => WhatsAppDigitos.Length >= 10;

    public bool TemContato => TemEmail || TemWhatsApp;

    /// <summary>Só os dígitos do WhatsApp.</summary>
    public string WhatsAppDigitos => new((WhatsApp ?? string.Empty).Where(char.IsDigit).ToArray());

    /// <summary>Link que abre a conversa (nulo sem WhatsApp).</summary>
    public string? WhatsAppLink => TemWhatsApp ? $"https://wa.me/{WhatsAppDigitos}" : null;

    /// <summary>"(71) 99999-8888" para número brasileiro (55 + DDD + 8 ou 9 dígitos); senão, "+" e os dígitos.</summary>
    public string? WhatsAppFormatado
    {
        get
        {
            if (!TemWhatsApp)
                return null;

            var d = WhatsAppDigitos;
            if (d.StartsWith("55") && d.Length is 12 or 13)
                d = d[2..];
            if (d.Length is 10 or 11)
                return $"({d[..2]}) {d[2..^4]}-{d[^4..]}";

            return "+" + WhatsAppDigitos;
        }
    }

    /// <summary>"suporte@... ou WhatsApp (71) 99999-8888" (vazio sem contato).</summary>
    public string TextoContato =>
        string.Join(" ou ", new[] { TemEmail ? Email.Trim() : null, TemWhatsApp ? $"WhatsApp {WhatsAppFormatado}" : null }
            .Where(x => x is not null));
}
