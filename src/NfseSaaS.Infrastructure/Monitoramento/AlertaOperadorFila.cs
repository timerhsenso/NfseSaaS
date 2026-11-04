using System.Threading.Channels;

namespace NfseSaaS.Infrastructure.Monitoramento;

/// <summary>
/// Um problema que o operador da plataforma precisa saber: erro registrado
/// no log (Error/Fatal) ou job que falhou de vez.
/// </summary>
/// <param name="Titulo">Mensagem já renderizada do log (uma linha).</param>
/// <param name="Origem">Categoria do log (classe que registrou), quando houver.</param>
/// <param name="Excecao">Tipo, mensagem e pilha da exceção, quando houver.</param>
/// <param name="ChaveAgrupamento">Ocorrências com a mesma chave viram UM e-mail por janela (ver AlertaOperadorHostedService).</param>
public sealed record AlertaOperador(
    DateTimeOffset Quando,
    string Nivel,
    string Titulo,
    string? Origem,
    string? Excecao,
    string? CorrelationId,
    string ChaveAgrupamento);

/// <summary>
/// Fila em memória entre quem detecta o problema (o sink do Serilog, no Web)
/// e o AlertaOperadorHostedService, que manda os e-mails. Escrever aqui
/// nunca bloqueia nem lança: o log não pode travar por causa de alerta.
/// Limitada — numa avalanche de erros, os mais antigos são descartados (o
/// log em arquivo continua completo).
/// </summary>
public sealed class AlertaOperadorFila
{
    private const int Capacidade = 500;

    private readonly Channel<AlertaOperador> _canal = Channel.CreateBounded<AlertaOperador>(
        new BoundedChannelOptions(Capacidade) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public void Publicar(AlertaOperador alerta) => _canal.Writer.TryWrite(alerta);

    public IAsyncEnumerable<AlertaOperador> LerTodosAsync(CancellationToken cancellationToken) =>
        _canal.Reader.ReadAllAsync(cancellationToken);
}
