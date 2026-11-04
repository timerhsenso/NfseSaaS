namespace NfseSaaS.Application.Abstractions;

/// <summary>Resultado de uma tentativa de envio — ErroDetalhe é null quando Sucesso é true.</summary>
public sealed record ResultadoEnvioEmail(bool Sucesso, string? ErroDetalhe);

/// <summary>Arquivo anexado a um e-mail (ex.: PDF e XML da nota).</summary>
public sealed record AnexoEmail(string NomeArquivo, string ContentType, byte[] Conteudo);

/// <summary>
/// Abstração de envio de e-mail. Implementação real via SMTP em
/// Infrastructure (ver EmailOptions/SmtpEmailSender) — Application nunca
/// conhece MailKit nem detalhes de transporte.
/// </summary>
public interface IEmailSender
{
    /// <summary>Nunca lança exceção — qualquer falha (SMTP não configurado, erro de rede, autenticação) vira ResultadoEnvioEmail.Sucesso=false com ErroDetalhe preenchido.</summary>
    Task<ResultadoEnvioEmail> EnviarAsync(string destinatarioEmail, string assunto, string corpoHtml, CancellationToken cancellationToken);

    /// <summary>Mesmo contrato do método acima, com anexos (lista vazia = sem anexo).</summary>
    Task<ResultadoEnvioEmail> EnviarAsync(string destinatarioEmail, string assunto, string corpoHtml, IReadOnlyCollection<AnexoEmail> anexos, CancellationToken cancellationToken);
}
