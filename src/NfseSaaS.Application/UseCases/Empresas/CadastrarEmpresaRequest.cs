namespace NfseSaaS.Application.UseCases.Empresas;

/// <summary>DTO de entrada para cadastro de empresa emissora de NFS-e.</summary>
public sealed record CadastrarEmpresaRequest(
    string Cnpj,
    string RazaoSocial,
    string? NomeFantasia,
    string InscricaoMunicipal,
    string CodigoMunicipio,
    string Telefone,
    string Email,
    string Cep,
    string Logradouro,
    string Numero,
    string? Complemento,
    string Bairro,
    string Uf,
    string OpSimpNac,
    string RegApTribSN,
    string RegEspTrib,
    string TribIssqn,
    string TpRetIssqn,
    string CstPisCofins,
    string TpRetPisCofins,
    string PercentualTotalTributosSimplesNacional);
