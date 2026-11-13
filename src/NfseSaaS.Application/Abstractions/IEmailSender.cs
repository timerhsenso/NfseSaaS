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
    /// <summary>
    /// Tamanho máximo da parte da empresa: o que cabe na caixa de entrada do
    /// celular antes de "via MicoEmpresa". Mais longo = só o começo do nome.
    /// </summary>
    public const int TamanhoMaximoEmpresa = 30;

    private static readonly string[] Separadores = { " - ", " – ", " — ", " | ", ", ", " / " };

    /// <summary>
    /// "RHSENSO via MicoEmpresa": o nome fantasia do cadastro da empresa
    /// (senão a razão social). Tira o que confunde o cabeçalho (aspas, &lt; &gt;,
    /// @, quebras de linha). Sem nome = só a plataforma.
    /// </summary>
    public static string NomeExibicao(string? nomeFantasia, string? razaoSocial, string plataforma)
    {
        var empresa = string.IsNullOrWhiteSpace(nomeFantasia) ? razaoSocial : nomeFantasia;
        if (string.IsNullOrWhiteSpace(empresa))
            return plataforma;

        var limpo = new string(empresa.Where(c => !char.IsControl(c) && c is not ('"' or '<' or '>' or '@')).ToArray());
        limpo = string.Join(' ', limpo.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        limpo = PrimeiraPorcao(limpo);
        return limpo.Length == 0 ? plataforma : $"{limpo} via {plataforma}";
    }

    /// <summary>
    /// Nome longo: a primeira porção. "RHSENSO - SISTEMAS E CONSULTORIA EM RH" →
    /// "RHSENSO"; sem separador, as palavras inteiras que cabem; uma palavra
    /// só maior que o limite é cortada.
    /// </summary>
    public static string PrimeiraPorcao(string nome)
    {
        if (nome.Length <= TamanhoMaximoEmpresa)
            return nome;

        foreach (var separador in Separadores)
        {
            var posicao = nome.IndexOf(separador, StringComparison.Ordinal);
            if (posicao > 0)
            {
                var antes = nome[..posicao].Trim();
                if (antes.Length is > 0 and <= TamanhoMaximoEmpresa)
                    return antes;
            }
        }

        var resultado = string.Empty;
        foreach (var palavra in nome.Split(' '))
        {
            var proximo = resultado.Length == 0 ? palavra : $"{resultado} {palavra}";
            if (proximo.Length > TamanhoMaximoEmpresa)
                break;
            resultado = proximo;
        }
        return resultado.Length > 0 ? resultado.TrimEnd('-', '–', '—', ',', '/', '|', ' ') : nome[..TamanhoMaximoEmpresa].TrimEnd();
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
