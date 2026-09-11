namespace NfseSaaS.Application.UseCases.NotaMensal;

public sealed record ItemResultadoNotaMensalResponse(
    Guid ContratoId,
    string ClienteNome,
    string DescricaoNota,
    bool Sucesso,
    Guid? NfseId,
    string? NumeroNfse,
    string? Mensagem);
