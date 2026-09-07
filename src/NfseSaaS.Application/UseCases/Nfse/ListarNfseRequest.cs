using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Nfse;

public sealed record ListarNfseRequest(
    Guid? EmpresaId = null,
    NfseStatus? Status = null,
    int Page = 1,
    int PageSize = 20);
