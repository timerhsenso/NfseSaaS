namespace NfseSaaS.Application.UseCases.Clientes;

/// <summary>DTO de entrada para cadastro de cliente (implementação na Fase 2).</summary>
public sealed record CadastrarClienteRequest(
    Guid EmpresaId,
    string CpfCnpj,
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
    // C10: true = disponível para todas as empresas da conta; false = só a
    // EmpresaId acima; nulo = padrão da conta (RegrasClienteCompartilhado).
    bool? Compartilhado = null) : IDadosEnvioEmailCliente;
