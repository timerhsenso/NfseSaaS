using System.Threading.Channels;

namespace NfseSaaS.Infrastructure.Email;

/// <summary>Um item pronto pra enviar — já tem o corpo montado, o EmailLog associado só é atualizado depois do envio (ver EmailDispatchHostedService).</summary>
public sealed record ItemFilaEmail(Guid EmailLogId, string Destinatario, string Assunto, string CorpoHtml);

/// <summary>
/// Fila de envio de e-mail em memória (Channel — não persiste em disco).
/// Singleton: uma instância só pra aplicação inteira, compartilhada entre
/// quem enfileira (EmailQueueService, dentro de uma requisição) e quem
/// consome (EmailDispatchHostedService, em background).
///
/// Fila em memória é perdida se a aplicação reiniciar no meio — por isso
/// todo item já existe como EmailLog.Status=Pendente no banco ANTES de
/// entrar aqui, e EmailPendenteRecuperador reenfileira no próximo boot
/// tudo que ficou Pendente (ver Program.cs).
/// </summary>
public interface IBackgroundEmailQueue
{
    void Enfileirar(ItemFilaEmail item);

    IAsyncEnumerable<ItemFilaEmail> LerTodosAsync(CancellationToken cancellationToken);
}

public sealed class BackgroundEmailQueue : IBackgroundEmailQueue
{
    private readonly Channel<ItemFilaEmail> _canal = Channel.CreateUnbounded<ItemFilaEmail>();

    public void Enfileirar(ItemFilaEmail item) => _canal.Writer.TryWrite(item);

    public IAsyncEnumerable<ItemFilaEmail> LerTodosAsync(CancellationToken cancellationToken) =>
        _canal.Reader.ReadAllAsync(cancellationToken);
}
