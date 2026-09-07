namespace NfseSaaS.Application.UseCases.Clientes;

/// <summary>DTO de entrada para cadastro de cliente (implementação na Fase 2).</summary>
public sealed record CadastrarClienteRequest(
    Guid EmpresaId,
    string CpfCnpj,
    string Nome,
    string? Email,
    string? Telefone,
    string CodigoMunicipio,
    string Cep,
    string Logradouro,
    string Numero,
    string Bairro);
