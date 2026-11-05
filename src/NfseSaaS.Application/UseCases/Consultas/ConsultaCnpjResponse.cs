namespace NfseSaaS.Application.UseCases.Consultas;

/// <summary>
/// Só os campos com correspondência DIRETA e SEM AMBIGUIDADE no
/// cadastro de Empresa deste SaaS. O regime tributário NÃO é preenchido
/// como definitivo: a Receita informa se a empresa é optante do
/// Simples/MEI (situação na data da consulta) e isso vira só uma SUGESTÃO
/// de opSimpNac (OpcaoSimplesSugerida) — regApTribSN/regEspTrib e o resto
/// o usuário confirma no cadastro da Empresa (Empresa.PendenciasCadastro
/// mantém "dados fiscais" pendente até lá). Inscrição Municipal nunca vem
/// daqui — é dado do município, não da Receita Federal.
/// </summary>
public sealed record ConsultaCnpjResponse(
    string Cnpj,
    string RazaoSocial,
    string? NomeFantasia,
    string? Logradouro,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Cep,
    string? CodigoMunicipio,
    string? Uf,
    string? Telefone,
    string? Email,
    string? SituacaoCadastral,
    string? Municipio = null,
    bool? OptanteSimples = null,
    bool? OptanteMei = null)
{
    /// <summary>A Receita mostra a empresa como ativa (inapta, suspensa ou baixada não emitem).</summary>
    public bool Ativa => SituacaoCadastral is null || SituacaoCadastral.Trim().Equals("ATIVA", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Sugestão de OpSimpNac a partir da Receita: "2" MEI, "3" Simples (ME/EPP),
    /// "1" não optante; nulo quando a consulta não informou. É só sugestão:
    /// o usuário confirma no cadastro da Empresa.
    /// </summary>
    public string? OpcaoSimplesSugerida => OptanteMei == true ? "2" : OptanteSimples == true ? "3" : OptanteSimples == false ? "1" : null;
}
