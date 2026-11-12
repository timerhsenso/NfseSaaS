namespace NfseSaaS.Application.Abstractions;

/// <summary>
/// E-mail a enfileirar, com os campos opcionais que nem todo tipo usa.
/// </summary>
/// <param name="NfseId">Nota cujo PDF e XML vão anexados — gerados na hora do envio (ver EmailLog.NfseId).</param>
/// <param name="ChaveDeduplicacao">Aviso que só pode sair uma vez — ver EmailLog.ChaveDeduplicacao.</param>
/// <param name="ResponderPara">Reply-To — ver EmailLog.ResponderPara.</param>
/// <param name="Copias">Endereços em cópia — ver EmailLog.Copias.</param>
/// <param name="CopiasOcultas">true = cópias em Cco.</param>
/// <param name="Segredos">Tokens do corpo: o e-mail sai com eles, mas o EmailLog grava o corpo com eles escondidos (MascaramentoEmail).</param>
public sealed record EmailParaEnfileirar(
    string Tipo,
    Guid? UsuarioId,
    string Destinatario,
    string Assunto,
    string CorpoHtml,
    Guid? NfseId = null,
    string? ChaveDeduplicacao = null,
    string? ResponderPara = null,
    IReadOnlyCollection<string>? Segredos = null,
    IReadOnlyCollection<string>? Copias = null,
    bool CopiasOcultas = false,
    string? CopiaOcultaFixa = null);

/// <summary>
/// Ponto único que os controllers usam pra "enviar" um e-mail — na
/// prática só registra o EmailLog (Status=Pendente) e enfileira; o envio
/// de verdade acontece em segundo plano (ver EmailDispatchHostedService
/// em Infrastructure), então a requisição HTTP nunca espera o SMTP.
/// </summary>
public interface IEmailQueueService
{
    /// <returns>Id do EmailLog criado — útil pra quem chamou linkar direto na tela de E-mails, se quiser.</returns>
    Task<Guid> EnfileirarAsync(string tipo, Guid? usuarioId, string destinatario, string assunto, string corpoHtml, CancellationToken cancellationToken);

    /// <inheritdoc cref="EnfileirarAsync(string, Guid?, string, string, string, CancellationToken)"/>
    Task<Guid> EnfileirarAsync(EmailParaEnfileirar email, CancellationToken cancellationToken);

    /// <summary>True se já existe e-mail registrado com esta chave (ver EmailLog.ChaveDeduplicacao), no tenant atual.</summary>
    Task<bool> JaEnfileiradoAsync(string chaveDeduplicacao, CancellationToken cancellationToken);
}
