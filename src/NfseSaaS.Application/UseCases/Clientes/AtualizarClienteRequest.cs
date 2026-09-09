namespace NfseSaaS.Application.UseCases.Clientes;

/// <summary>CpfCnpj não é editável — mesmo raciocínio do Cnpj da Empresa.</summary>
public sealed record AtualizarClienteRequest(
    string Nome,
    string? Email,
    string? Telefone,
    string CodigoMunicipio,
    string Cep,
    string Logradouro,
    string Numero,
    string? Complemento,
    string Bairro,
    string Uf);
