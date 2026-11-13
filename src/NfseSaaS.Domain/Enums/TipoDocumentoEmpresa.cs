namespace NfseSaaS.Domain.Enums;

/// <summary>
/// DOC1: tipos de documento da Empresa (certidões e cadastros). Os nomes,
/// a validade sugerida e o link do órgão ficam em CatalogoDocumentosEmpresa
/// (Application). Valor numérico gravado no banco: não renumerar.
/// </summary>
public enum TipoDocumentoEmpresa
{
    /// <summary>Certidão de débitos federais e dívida ativa da União (Receita Federal/PGFN).</summary>
    CndFederal = 1,

    /// <summary>Certidão de débitos estaduais (SEFAZ do estado).</summary>
    CndEstadual = 2,

    /// <summary>Certidão de débitos municipais (prefeitura).</summary>
    CndMunicipal = 3,

    /// <summary>Certificado de Regularidade do FGTS (Caixa).</summary>
    CrfFgts = 4,

    /// <summary>Certidão Negativa de Débitos Trabalhistas (TST).</summary>
    Cndt = 5,

    /// <summary>Comprovante de opção pelo Simples Nacional.</summary>
    SimplesNacional = 6,

    /// <summary>Contrato social ou última alteração consolidada.</summary>
    ContratoSocial = 7,

    /// <summary>Comprovante de inscrição no CNPJ (cartão CNPJ).</summary>
    CartaoCnpj = 8,

    /// <summary>Qualquer outro documento, com nome dado pelo usuário.</summary>
    Outro = 99
}
