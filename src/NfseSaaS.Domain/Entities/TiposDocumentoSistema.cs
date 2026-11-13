namespace NfseSaaS.Domain.Entities;

/// <summary>
/// DOC4: números fixos dos tipos de documento que existiam antes do catálogo
/// no banco (TipoDocumento). Já estão gravados em documentos_empresa.Tipo e em
/// Cliente.DocumentosExigidos: não renumerar. Os demais tipos do catálogo
/// são criados pelo seeder e pelo operador (Id a partir de 1000).
/// </summary>
public static class TiposDocumentoSistema
{
    public const int CndFederal = 1;
    public const int CndEstadual = 2;
    public const int CndMunicipal = 3;
    public const int CrfFgts = 4;
    public const int Cndt = 5;
    public const int SimplesNacional = 6;
    public const int ContratoSocial = 7;
    public const int CartaoCnpj = 8;

    /// <summary>Qualquer outro documento, com o nome dado pelo usuário. Nunca entra na escolha da empresa.</summary>
    public const int Outro = 99;

    /// <summary>Ids criados pelo banco (seeder e operador) começam aqui, longe dos fixos.</summary>
    public const int PrimeiroIdLivre = 1000;
}
