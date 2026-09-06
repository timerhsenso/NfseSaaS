namespace NfseSaaS.Nacional.Models;

/// <summary>
/// Dados do prestador (Empresa emissora) necessários para a DPS. Na POC,
/// todos esses valores eram fixos no código — aqui vêm de fora porque, num
/// SaaS multiempresa, cada Empresa tem seu próprio CNPJ, IM e regime
/// tributário.
/// </summary>
public sealed record PrestadorDps(
    string Cnpj,
    string InscricaoMunicipal,
    string Telefone,
    string Email,
    string CodigoMunicipio,
    string OpSimpNac,
    string RegApTribSN,
    string RegEspTrib);

/// <summary>Identificação e endereço do tomador (Cliente) do serviço.</summary>
public sealed record TomadorDps(
    string CnpjOuCpf,
    string Nome,
    string CodigoMunicipio,
    string Cep,
    string Logradouro,
    string Numero,
    string Bairro);

/// <summary>
/// Campos de tributação da DPS. Valores diretamente parametrizados a
/// partir do que já foi validado na POC (emissão real aceita pela SEFIN,
/// HTTP 201) — a determinação automática desses campos a partir do regime
/// tributário real de cada Empresa (cálculo fiscal propriamente dito) é
/// trabalho de fase futura, fora do escopo desta migração.
/// </summary>
public sealed record TributacaoDps(
    string TribIssqn,
    string TpRetIssqn,
    string CstPisCofins,
    string TpRetPisCofins,
    string PercentualTotalTributosSimplesNacional);

/// <summary>
/// Dados necessários para gerar uma DPS. Modelo próprio do módulo
/// NfseSaaS.Nacional — deliberadamente não é a entidade Nfse do Domain,
/// para manter este módulo isolado (ver Requisito 4/5 da especificação).
/// </summary>
public sealed record DpsRequest(
    PrestadorDps Prestador,
    TomadorDps Tomador,
    TributacaoDps Tributacao,
    int NumeroDps,
    string SerieDps,
    DateOnly DataCompetencia,
    decimal Valor,
    string CodigoTributacaoNacional,
    string CodigoNbs,
    string DescricaoServico);