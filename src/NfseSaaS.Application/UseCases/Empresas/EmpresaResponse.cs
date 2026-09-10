using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Empresas;

public sealed record EmpresaResponse(
    Guid Id,
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
    string PercentualTotalTributosSimplesNacional,
    int DiasAlertaReajusteContratoPadrao,
    bool Ativo,
    // Serializa como int (1/2), mesmo padrão já usado por NfseStatus em
    // NfseResponse — sem JsonStringEnumConverter configurado no projeto.
    // O front traduz pra rótulo, igual ROTULOS_STATUS já faz em nfse.js.
    TipoAmbiente TipoAmbiente,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
