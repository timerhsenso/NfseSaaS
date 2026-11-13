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
    decimal? AliquotaIss,
    string CstPisCofins,
    string PercentualTotalTributosSimplesNacional,
    // C6: frase de dispensa de retenção na descrição da nota (Simples).
    bool AvisoDispensaRetencaoNaDescricao = false);
