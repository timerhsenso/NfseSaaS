namespace NfseSaaS.Application.UseCases.Contratos;

public sealed record ListarContratosRequest(
    Guid EmpresaId,
    int Page = 1,
    int PageSize = 20,
    string? Busca = null,
    bool IncluirInativos = false,
    Guid? ClienteId = null);
