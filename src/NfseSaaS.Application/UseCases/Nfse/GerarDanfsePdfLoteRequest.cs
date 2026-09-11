namespace NfseSaaS.Application.UseCases.Nfse;

public sealed record GerarDanfsePdfLoteRequest(IReadOnlyList<Guid> NfseIds);
