namespace NfseSaaS.Application.UseCases.Empresas;

public sealed record ListarEmpresasRequest(
    int Page = 1,
    int PageSize = 20,
    string? Busca = null,
    bool IncluirInativas = false);
