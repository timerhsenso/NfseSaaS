namespace NfseSaaS.Application.UseCases.Servicos;

public sealed record ListarServicosRequest(
    Guid EmpresaId,
    int Page = 1,
    int PageSize = 20,
    string? Busca = null,
    bool IncluirInativos = false);
