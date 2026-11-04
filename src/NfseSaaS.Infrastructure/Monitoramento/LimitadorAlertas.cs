namespace NfseSaaS.Infrastructure.Monitoramento;

/// <summary>
/// Decide se um alerta vira e-mail agora, para o operador não receber uma
/// enxurrada quando o mesmo erro se repete:
/// - mesma chave: no máximo 1 e-mail a cada <see cref="JanelaPorChave"/>; as
///   repetições no meio são contadas e aparecem no próximo e-mail dela;
/// - no total: no máximo <see cref="MaximoPorHora"/> e-mails por hora.
/// Não é thread-safe de propósito: só o AlertaOperadorHostedService (um
/// leitor) usa.
/// </summary>
public sealed class LimitadorAlertas
{
    public static readonly TimeSpan JanelaPorChave = TimeSpan.FromMinutes(30);

    public const int MaximoPorHora = 20;

    private readonly Dictionary<string, (DateTimeOffset UltimoEnvio, int Suprimidos)> _porChave = new();
    private readonly Queue<DateTimeOffset> _enviosNaUltimaHora = new();

    /// <returns>Enviar=false: segure. Enviar=true: mande, citando as <c>Suprimidos</c> repetições que ficaram sem e-mail desde o último.</returns>
    public (bool Enviar, int Suprimidos) Avaliar(string chave, DateTimeOffset agora)
    {
        while (_enviosNaUltimaHora.Count > 0 && agora - _enviosNaUltimaHora.Peek() >= TimeSpan.FromHours(1))
            _enviosNaUltimaHora.Dequeue();

        _porChave.TryGetValue(chave, out var estado);

        var dentroDaJanela = estado.UltimoEnvio != default && agora - estado.UltimoEnvio < JanelaPorChave;
        if (dentroDaJanela || _enviosNaUltimaHora.Count >= MaximoPorHora)
        {
            _porChave[chave] = (estado.UltimoEnvio, estado.Suprimidos + 1);
            return (false, 0);
        }

        _porChave[chave] = (agora, 0);
        _enviosNaUltimaHora.Enqueue(agora);

        // Limpeza simples para o dicionário não crescer sem fim.
        if (_porChave.Count > 1000)
        {
            foreach (var antiga in _porChave.Where(p => agora - p.Value.UltimoEnvio > TimeSpan.FromDays(1)).Select(p => p.Key).ToList())
                _porChave.Remove(antiga);
        }

        return (true, estado.Suprimidos);
    }
}
