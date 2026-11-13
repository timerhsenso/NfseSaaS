namespace NfseSaaS.Application.UseCases.Clientes;

public sealed record ClienteResponse(
    Guid Id,
    Guid? EmpresaId,
    string CpfCnpj,
    string Nome,
    string Apelido,
    string? Email,
    string? Telefone,
    string CodigoMunicipio,
    string Cep,
    string Logradouro,
    string Numero,
    string? Complemento,
    string Bairro,
    string Uf,
    bool Ativo,
    bool RetemIss,
    bool EnviarNfsePorEmail,
    IReadOnlyList<string> EmailsCopia,
    bool CopiaOculta,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    // Modelo do e-mail da nota só deste cliente; nulo = o da empresa.
    Guid? ModeloEmailNotaId = null)
{
    /// <summary>C10: true = disponível para todas as empresas da conta (EmpresaId nulo).</summary>
    public bool Compartilhado => EmpresaId is null;
}
