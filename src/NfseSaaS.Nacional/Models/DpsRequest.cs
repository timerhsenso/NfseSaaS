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

/// <summary>
/// Identificação e endereço do tomador (Cliente) do serviço. O endereço é
/// opcional (C3): todos os campos em branco = a DPS vai sem o grupo end
/// (só aceito pela SEFIN para pessoa física, sem retenção e sem IBS/CBS).
/// Complemento, telefone e e-mail vão quando preenchidos.
/// </summary>
public sealed record TomadorDps(
    string CnpjOuCpf,
    string Nome,
    string CodigoMunicipio,
    string Cep,
    string Logradouro,
    string Numero,
    string Bairro,
    string? Complemento = null,
    string? Telefone = null,
    string? Email = null)
{
    /// <summary>Nenhum campo do endereço preenchido: a DPS vai sem o grupo end.</summary>
    public bool SemEndereco =>
        new[] { CodigoMunicipio, Cep, Logradouro, Numero, Bairro, Complemento }.All(string.IsNullOrWhiteSpace);
}

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
    string PercentualTotalTributosSimplesNacional,
    // pAliq (%) no formato "0.00" — obrigatório quando TpRetIssqn = "2"
    // (ISS retido pelo tomador). Vazio = elemento não enviado.
    string AliquotaIss = "");

/// <summary>
/// Dados necessários para gerar uma DPS. Modelo próprio do módulo
/// NfseSaaS.Nacional — deliberadamente não é a entidade Nfse do Domain,
/// para manter este módulo isolado (ver Requisito 4/5 da especificação).
/// </summary>
/// <param name="TpAmb">
/// tpAmb da DPS: "1" = Produção, "2" = Homologação. Deliberadamente uma
/// string pronta pro XML, não um enum do Domain — este módulo Nacional
/// não conhece o Domain (ver arquitetura). Quem decide o valor é o
/// chamador (EmitirNfseUseCase, a partir de Empresa.TipoAmbiente), nunca
/// este módulo nem o appsettings.
/// </param>
/// <summary>
/// Grupo IBSCBS da DPS (Reforma Tributária, leiaute NT 009). Só a
/// classificação: os valores a SEFIN calcula. Nulo = grupo não vai.
/// </summary>
/// <param name="CIndOp">Indicador da operação (Anexo VII); nulo quando o CST não tem tributação a declarar.</param>
/// <param name="Cst">3 primeiros dígitos do cClassTrib.</param>
/// <param name="ComTributacao">ind_gIBSCBS = 1: escreve o subgrupo gIBSCBS (E0960); 0: não escreve (E0961).</param>
public sealed record IbsCbsDps(string? CIndOp, string Cst, string CClassTrib, bool ComTributacao);

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
    string DescricaoServico,
    string TpAmb,
    IbsCbsDps? IbsCbs = null);