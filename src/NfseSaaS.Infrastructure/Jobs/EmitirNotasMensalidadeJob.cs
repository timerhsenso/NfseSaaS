using Hangfire;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.UseCases.Cobranca;

namespace NfseSaaS.Infrastructure.Jobs;

/// <summary>
/// NFS-e da mensalidade (N1) — atributos do Hangfire na interface (ver
/// ImportarIndicesEconomicosJob). Disparado pelo webhook a cada pagamento
/// confirmado e, todo dia, como repescagem. Sem NotaMensalidade:EmpresaId e
/// NotaMensalidade:ServicoId preenchidos, não faz nada.
/// </summary>
public interface IEmitirNotasMensalidadeJob
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 0)]
    Task ExecutarAsync();
}

public sealed class EmitirNotasMensalidadeJob : IEmitirNotasMensalidadeJob
{
    public const string JobId = "emitir-notas-mensalidade";

    /// <summary>08:10 de Brasília (SEFIN em horário comercial, operador por perto).</summary>
    public const string Cron = "10 8 * * *";

    private readonly INotasMensalidadeUseCase _notas;
    private readonly ILogger<EmitirNotasMensalidadeJob> _logger;

    public EmitirNotasMensalidadeJob(INotasMensalidadeUseCase notas, ILogger<EmitirNotasMensalidadeJob> logger)
    {
        _notas = notas;
        _logger = logger;
    }

    public async Task ExecutarAsync()
    {
        // Cada falha já foi para o log como erro (alerta ao operador); o job não relança.
        var r = await _notas.ExecutarAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        if (r.Desligado)
            _logger.LogDebug("NFS-e da mensalidade desligada (NotaMensalidade:EmpresaId/ServicoId vazios).");
    }
}
