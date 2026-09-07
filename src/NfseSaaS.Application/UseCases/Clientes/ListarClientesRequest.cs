namespace NfseSaaS.Application.UseCases.Clientes;

public sealed record ListarClientesRequest(
    Guid EmpresaId,
    int Page = 1,
    int PageSize = 20,
    string? Busca = null,
    bool IncluirInativos = false);
