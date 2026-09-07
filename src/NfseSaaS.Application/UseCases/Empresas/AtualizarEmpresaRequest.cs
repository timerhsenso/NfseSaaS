namespace NfseSaaS.Application.UseCases.Empresas;

/// <summary>
/// Cnpj não é editável: é a identidade fiscal da Empresa, vinculada ao
/// certificado digital cadastrado. Para corrigir um Cnpj errado, cadastre
/// uma nova Empresa.
/// </summary>
public sealed record AtualizarEmpresaRequest(
    string RazaoSocial,
    string? NomeFantasia,
    string InscricaoMunicipal,
    string CodigoMunicipio,
    string Telefone,
    string Email,
    string OpSimpNac,
    string RegApTribSN,
    string RegEspTrib,
    string TribIssqn,
    string TpRetIssqn,
    string CstPisCofins,
    string TpRetPisCofins,
    string PercentualTotalTributosSimplesNacional);
