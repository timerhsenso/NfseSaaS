using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using NfseSaaS.Application.Abstractions;

namespace NfseSaaS.Infrastructure.Email;

/// <summary>
/// Envio de e-mail real via SMTP, usando MailKit (biblioteca recomendada
/// pela própria documentação da Microsoft em vez do SmtpClient do .NET,
/// que está em modo de manutenção e não deve ser usado em código novo).
///
/// Falha de envio NUNCA propaga exceção — só loga e devolve false. E-mail
/// é tratado como "melhor esforço": se falhar, a operação que chamou (ex.:
/// convidar um usuário) já aconteceu de verdade no banco e continua
/// válida, só não chegou por e-mail dessa vez.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> EnviarAsync(string destinatarioEmail, string assunto, string corpoHtml, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.SmtpHost))
        {
            _logger.LogWarning("Email:SmtpHost não configurado — e-mail para {Destinatario} NÃO foi enviado.", destinatarioEmail);
            return false;
        }

        var mensagem = new MimeMessage();
        mensagem.From.Add(new MailboxAddress(_options.RemetenteNome, _options.RemetenteEmail));
        mensagem.To.Add(MailboxAddress.Parse(destinatarioEmail));
        mensagem.Subject = assunto;
        mensagem.Body = new TextPart("html") { Text = corpoHtml };

        try
        {
            using var client = new SmtpClient();

            await client.ConnectAsync(
                _options.SmtpHost,
                _options.SmtpPort,
                _options.UsarSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.Usuario))
                await client.AuthenticateAsync(_options.Usuario, _options.Senha, cancellationToken);

            await client.SendAsync(mensagem, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao enviar e-mail para {Destinatario}.", destinatarioEmail);
            return false;
        }
    }
}
