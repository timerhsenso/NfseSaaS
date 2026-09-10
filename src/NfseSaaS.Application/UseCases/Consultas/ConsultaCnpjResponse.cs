namespace NfseSaaS.Application.UseCases.Consultas;

/// <summary>
/// Só os campos com correspondência DIRETA e SEM AMBIGUIDADE no
/// cadastro de Empresa deste SaaS. Deliberadamente NÃO inclui regime
/// tributário (opSimpNac/regApTribSN/regEspTrib) — a Receita Federal
/// informa se a empresa é optante do Simples/MEI como um dado cadastral
/// próprio dela (situação na data da consulta), mas os campos da NT
/// 008/DPS têm um código específico (1/2/3) que não é um mapeamento
/// direto e confiável a partir disso; o usuário confirma esse regime
/// manualmente. Inscrição Municipal também nunca vem daqui — é dado do
/// município, não da Receita Federal.
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
    string? SituacaoCadastral);
