namespace NfseSaaS.Application.UseCases.NfseEventos;

public sealed record NfseEventoResponse(
    Guid Id,
    Guid NfseId,
    string Tipo,
    string? Codigo,
    string? Mensagem,
    DateTimeOffset DataHora);
