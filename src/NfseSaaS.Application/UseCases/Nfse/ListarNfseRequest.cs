using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Nfse;

public sealed record ListarNfseRequest(
    Guid? EmpresaId = null,
    NfseStatus? Status = null,
    Guid? ClienteId = null,
    DateOnly? DataInicio = null,
    DateOnly? DataFim = null,
    int Page = 1,
    int PageSize = 20,
    // CR1: "confirmado" = algum destinatário confirmou; "pendente" = enviada por e-mail e ninguém confirmou.
    string? Recebimento = null);
