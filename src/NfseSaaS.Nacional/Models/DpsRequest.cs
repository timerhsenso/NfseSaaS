namespace NfseSaaS.Nacional.Models;

/// <summary>
/// Dados necessários para gerar uma DPS. Modelo próprio do módulo
/// NfseSaaS.Nacional — deliberadamente não é a entidade Nfse do Domain,
/// para manter este módulo isolado (ver Requisito 4/5 da especificação).
/// Corresponde ao DpsRequest já validado na POC.
/// </summary>
public sealed record DpsRequest(
    string CnpjPrestador,
    string InscricaoMunicipalPrestador,
    string CodigoMunicipioPrestador,
    string CnpjOuCpfTomador,
    string NomeTomador,
    int NumeroDps,
    string SerieDps,
    DateOnly DataCompetencia,
    decimal Valor,
    string CodigoTributacaoNacional,
    string DescricaoServico);
