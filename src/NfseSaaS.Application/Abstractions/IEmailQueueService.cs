namespace NfseSaaS.Application.Abstractions;

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
}
