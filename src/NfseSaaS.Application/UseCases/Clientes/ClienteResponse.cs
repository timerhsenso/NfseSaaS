namespace NfseSaaS.Application.UseCases.Clientes;

public sealed record ClienteResponse(
    Guid Id,
    Guid EmpresaId,
    string CpfCnpj,
    string Nome,
    string? Email,
    string? Telefone,
    string CodigoMunicipio,
    string Cep,
    string Logradouro,
    string Numero,
    string Bairro,
    bool Ativo,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
