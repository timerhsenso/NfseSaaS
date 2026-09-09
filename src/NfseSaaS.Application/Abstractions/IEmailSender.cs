namespace NfseSaaS.Application.Abstractions;

/// <summary>
/// Abstração de envio de e-mail. Implementação real via SMTP em
/// Infrastructure (ver EmailOptions/SmtpEmailSender) — Application nunca
/// conhece MailKit nem detalhes de transporte.
/// </summary>
public interface IEmailSender
{
    /// <returns>
    /// true se o e-mail foi efetivamente enviado; false se não foi
    /// (SMTP não configurado ou falha no envio) — nunca lança exceção,
    /// quem chama decide o que fazer com um envio que não saiu.
    /// </returns>
    Task<bool> EnviarAsync(string destinatarioEmail, string assunto, string corpoHtml, CancellationToken cancellationToken);
}
