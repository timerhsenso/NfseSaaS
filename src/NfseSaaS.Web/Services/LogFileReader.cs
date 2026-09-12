namespace NfseSaaS.Web.Services;

public sealed record ArquivoLog(string Nome, DateTimeOffset ModificadoEm, long TamanhoBytes);

public sealed record LinhasLog(string Arquivo, IReadOnlyList<string> Linhas, bool Truncado);

/// <summary>
/// Lê os arquivos gerados pelo sink Serilog.File configurado em
/// Program.cs (logs/log-.txt, rollingInterval Day → um arquivo por dia,
/// ex.: log-20260912.txt). Ferramenta puramente operacional pra dar
/// visibilidade de erro em produção sem precisar de terminal/SSH — não é
/// conceito de negócio, por isso vive direto no Web (mesmo raciocínio do
/// HealthChecks/PostgresHealthCheck), sem Application/Infrastructure.
///
/// IMPORTANTE (ambiente Docker): isto só mostra o que está no filesystem
/// do container ATUAL. Se "logs/" não for um volume persistente, um
/// redeploy zera o histórico — mas cobre o caso mais comum, que é
/// investigar um erro que acabou de acontecer na sessão atual.
/// </summary>
public interface ILogFileReader
{
    IReadOnlyList<ArquivoLog> ListarArquivos();

    LinhasLog LerUltimasLinhas(string nomeArquivo, int maximoLinhas, string? nivel, string? busca);
}

public sealed class LogFileReader : ILogFileReader
{
    // Cap de segurança: nunca varre mais que isto por leitura, mesmo se o
    // arquivo do dia estiver enorme — é um visualizador rápido, não uma
    // ferramenta de análise de log histórico completa (pra isso, baixar
    // o arquivo mesmo).
    private const int MaximoLinhasVarridas = 20_000;

    private readonly string _pastaLogs;

    public LogFileReader(IHostEnvironment environment)
    {
        _pastaLogs = Path.Combine(environment.ContentRootPath, "logs");
    }

    public IReadOnlyList<ArquivoLog> ListarArquivos()
    {
        if (!Directory.Exists(_pastaLogs))
            return Array.Empty<ArquivoLog>();

        return Directory.GetFiles(_pastaLogs, "log-*.txt")
            .Select(caminho => new FileInfo(caminho))
            .OrderByDescending(arquivo => arquivo.LastWriteTimeUtc)
            .Select(arquivo => new ArquivoLog(arquivo.Name, arquivo.LastWriteTimeUtc, arquivo.Length))
            .ToList();
    }

    public LinhasLog LerUltimasLinhas(string nomeArquivo, int maximoLinhas, string? nivel, string? busca)
    {
        var caminho = ResolverCaminhoSeguro(nomeArquivo);
        maximoLinhas = Math.Clamp(maximoLinhas, 1, 2000);

        // Fila circular: mantém só as últimas N linhas que passaram no
        // filtro, sem carregar o arquivo inteiro na memória.
        var janela = new Queue<string>(maximoLinhas);
        var totalVarrido = 0;

        foreach (var linha in File.ReadLines(caminho))
        {
            totalVarrido++;
            if (totalVarrido > MaximoLinhasVarridas)
                break;

            if (!string.IsNullOrEmpty(nivel) && !linha.Contains($"[{nivel}]", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(busca) && !linha.Contains(busca, StringComparison.OrdinalIgnoreCase))
                continue;

            if (janela.Count == maximoLinhas)
                janela.Dequeue();

            janela.Enqueue(linha);
        }

        return new LinhasLog(nomeArquivo, janela.ToList(), totalVarrido > MaximoLinhasVarridas);
    }

    /// <summary>
    /// Nunca confia no nome de arquivo vindo da query string: só aceita o
    /// padrão exato gerado pelo Serilog (log-AAAAMMDD.txt) e confirma que
    /// o caminho final continua dentro de logs/ — bloqueia qualquer
    /// tentativa de "../" ou caminho absoluto injetado no parâmetro.
    /// </summary>
    private string ResolverCaminhoSeguro(string nomeArquivo)
    {
        if (string.IsNullOrWhiteSpace(nomeArquivo) ||
            !System.Text.RegularExpressions.Regex.IsMatch(nomeArquivo, @"^log-\d{8}\.txt$"))
        {
            throw new FileNotFoundException("Nome de arquivo de log inválido.");
        }

        var caminhoCompleto = Path.GetFullPath(Path.Combine(_pastaLogs, nomeArquivo));
        var pastaLogsCompleta = Path.GetFullPath(_pastaLogs) + Path.DirectorySeparatorChar;

        if (!caminhoCompleto.StartsWith(pastaLogsCompleta, StringComparison.Ordinal) || !File.Exists(caminhoCompleto))
            throw new FileNotFoundException("Arquivo de log não encontrado.");

        return caminhoCompleto;
    }
}
