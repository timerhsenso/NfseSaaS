namespace NfseSaaS.Application.Abstractions;

/// <summary>Resultado de uma tentativa de envio — ErroDetalhe é null quando Sucesso é true.</summary>
public sealed record ResultadoEnvioEmail(bool Sucesso, string? ErroDetalhe);

/// <summary>Arquivo anexado a um e-mail (ex.: PDF e XML da nota).</summary>
public sealed record AnexoEmail(string NomeArquivo, string ContentType, byte[] Conteudo);

/// <summary>E-mail completo: com anexos e/ou "Responder para" (Reply-To).</summary>
/// <param name="RemetenteNome">
/// Nome de quem manda, aparece antes da plataforma ("LFMS via MicoEmpresa").
/// O endereço continua o do sistema (SPF/DMARC). Nulo = só o nome da plataforma.
/// </param>
public sealed record MensagemEmail(
    string Destinatario,
    string Assunto,
    string CorpoHtml,
    IReadOnlyCollection<AnexoEmail>? Anexos = null,
    string? ResponderPara = null,
    IReadOnlyCollection<string>? Copias = null,
    bool CopiasOcultas = false,
    string? CopiaOcultaFixa = null,
    string? RemetenteNome = null);

/// <summary>Nome do remetente no e-mail da nota: a empresa emissora "via" a plataforma.</summary>
public static class RegrasRemetenteEmail
{
    /// <summary>Tamanho máximo da parte da empresa (o resto do nome é da plataforma).</summary>
    public const int TamanhoMaximoEmpresa = 50;

    /// <summary>
    /// "LFMS via MicoEmpresa": nome fantasia, senão a razão social. Tira o que
    /// confunde o cabeçalho (aspas, &lt; &gt;, quebras de linha). Sem nome = só a plataforma.
    /// </summary>
    public static string NomeExibicao(string? nomeFantasia, string? razaoSocial, string plataforma)
    {
        var empresa = string.IsNullOrWhiteSpace(nomeFantasia) ? razaoSocial : nomeFantasia;
        if (string.IsNullOrWhiteSpace(empresa))
            return plataforma;

        var limpo = new string(empresa.Where(c => !char.IsControl(c) && c is not ('"' or '<' or '>' or '@')).ToArray());
        limpo = string.Join(' ', limpo.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (limpo.Length > TamanhoMaximoEmpresa)
            limpo = limpo[..TamanhoMaximoEmpresa].TrimEnd();
        return limpo.Length == 0 ? plataforma : $"{limpo} via {plataforma}";
    }
}

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

    /// <summary>
    /// Mesmo contrato, com Reply-To e cópias (Cc/Cco). A implementação
    /// padrão ignora os dois (implementações de teste não precisam mudar);
    /// o SMTP real usa.
    /// </summary>
    Task<ResultadoEnvioEmail> EnviarAsync(MensagemEmail mensagem, CancellationToken cancellationToken) =>
        EnviarAsync(mensagem.Destinatario, mensagem.Assunto, mensagem.CorpoHtml, mensagem.Anexos ?? Array.Empty<AnexoEmail>(), cancellationToken);
}
