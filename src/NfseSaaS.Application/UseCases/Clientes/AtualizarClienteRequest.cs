using NfseSaaS.Application.Fiscal.Retencoes;

namespace NfseSaaS.Application.UseCases.Clientes;

/// <summary>CpfCnpj não é editável — mesmo raciocínio do Cnpj da Empresa.</summary>
public sealed record AtualizarClienteRequest(
    string Nome,
    string Apelido,
    string? Email,
    string? Telefone,
    string? CodigoMunicipio,
    string? Cep,
    string? Logradouro,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Uf,
    bool RetemIss = false,
    bool EnviarNfsePorEmail = false,
    IReadOnlyList<string>? EmailsCopia = null,
    bool CopiaOculta = false,
    // C10: nulo = não muda "Disponível para". false num compartilhado =
    // passa a ser só da EmpresaId (a empresa atual da tela).
    bool? Compartilhado = null,
    Guid? EmpresaId = null,
    // C6: retenções federais que este tomador faz. Nulo = não muda.
    PercentuaisRetencao? RetencoesFederais = null) : IDadosEnvioEmailCliente;
