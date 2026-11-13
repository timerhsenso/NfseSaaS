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
/// Falha de envio NUNCA propaga exceção — só loga e devolve
/// ResultadoEnvioEmail.Sucesso=false com o detalhe do erro. E-mail é
/// tratado como "melhor esforço": se falhar, a operação que chamou (ex.:
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

    public Task<ResultadoEnvioEmail> EnviarAsync(string destinatarioEmail, string assunto, string corpoHtml, CancellationToken cancellationToken) =>
        EnviarAsync(destinatarioEmail, assunto, corpoHtml, Array.Empty<AnexoEmail>(), cancellationToken);

    public Task<ResultadoEnvioEmail> EnviarAsync(string destinatarioEmail, string assunto, string corpoHtml, IReadOnlyCollection<AnexoEmail> anexos, CancellationToken cancellationToken) =>
        EnviarAsync(new MensagemEmail(destinatarioEmail, assunto, corpoHtml, anexos), cancellationToken);

    public async Task<ResultadoEnvioEmail> EnviarAsync(MensagemEmail email, CancellationToken cancellationToken)
    {
        var destinatarioEmail = email.Destinatario;
        var assunto = email.Assunto;
        var corpoHtml = email.CorpoHtml;
        var anexos = email.Anexos ?? Array.Empty<AnexoEmail>();

        if (string.IsNullOrWhiteSpace(_options.SmtpHost))
        {
            _logger.LogWarning("Email:SmtpHost não configurado — e-mail para {Destinatario} NÃO foi enviado.", destinatarioEmail);
            return new ResultadoEnvioEmail(false, "Email:SmtpHost não configurado no servidor.");
        }

        // Endereço digitado no cadastro do tomador pode ser inválido — vira
        // falha registrada no EmailLog, não exceção solta no envio.
        if (!MailboxAddress.TryParse(destinatarioEmail, out var destinatario))
            return new ResultadoEnvioEmail(false, $"Endereço de e-mail inválido: '{destinatarioEmail}'.");

        var mensagem = new MimeMessage();
        // Nota fiscal: "LFMS via MicoEmpresa" — o cliente reconhece o fornecedor; o endereço segue o do sistema.
        var nomeRemetente = string.IsNullOrWhiteSpace(email.RemetenteNome) ? _options.RemetenteNome : email.RemetenteNome;
        mensagem.From.Add(new MailboxAddress(nomeRemetente, _options.RemetenteEmail));
        mensagem.To.Add(destinatario);
        // Reply-To inválido não impede o envio: só fica sem ele.
        if (!string.IsNullOrWhiteSpace(email.ResponderPara) && MailboxAddress.TryParse(email.ResponderPara, out var responderPara))
            mensagem.ReplyTo.Add(responderPara);
        AdicionarCopias(mensagem, email, _logger);
        mensagem.Subject = assunto;

        var corpo = new BodyBuilder { HtmlBody = corpoHtml };
        foreach (var anexo in anexos)
            corpo.Attachments.Add(anexo.NomeArquivo, anexo.Conteudo, ContentType.Parse(anexo.ContentType));
        mensagem.Body = corpo.ToMessageBody();

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

            return new ResultadoEnvioEmail(true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao enviar e-mail para {Destinatario}.", destinatarioEmail);
            return new ResultadoEnvioEmail(false, ex.Message);
        }
    }

    /// <summary>
    /// Cópias em Cc ou Cco. Endereço de cópia inválido (o cadastro valida,
    /// então só por dado antigo) não impede o envio ao destinatário
    /// principal: fica de fora e vai para o log.
    /// </summary>
    public static void AdicionarCopias(MimeMessage mensagem, MensagemEmail email, ILogger logger)
    {
        var lista = email.CopiasOcultas ? mensagem.Bcc : mensagem.Cc;
        foreach (var copia in email.Copias ?? Array.Empty<string>())
        {
            // TryParse aceita "fulano" sem domínio: exige o "@" também.
            if (MailboxAddress.TryParse(copia, out var endereco) && endereco.Address.Contains('@'))
                lista.Add(endereco);
            else
                logger.LogWarning("Cópia com endereço inválido ignorada: {Copia}.", copia);
        }

        // Cópia oculta fixa (modelo do e-mail da nota): sempre Cco, sem repetir quem já recebe.
        if (email.CopiaOcultaFixa is { } fixa && !string.IsNullOrWhiteSpace(fixa))
        {
            var jaRecebe = new[] { email.Destinatario }.Concat(email.Copias ?? Array.Empty<string>())
                .Any(e => string.Equals(e.Trim(), fixa.Trim(), StringComparison.OrdinalIgnoreCase));
            if (jaRecebe)
                return;
            if (MailboxAddress.TryParse(fixa.Trim(), out var endereco) && endereco.Address.Contains('@'))
                mensagem.Bcc.Add(endereco);
            else
                logger.LogWarning("Cópia oculta fixa com endereço inválido ignorada: {Copia}.", fixa);
        }
    }
}
