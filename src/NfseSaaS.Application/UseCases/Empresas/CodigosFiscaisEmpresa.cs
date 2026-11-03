namespace NfseSaaS.Application.UseCases.Empresas;

/// <summary>
/// Códigos aceitos pela SEFIN nos campos de regime/tributação do cadastro
/// de Empresa. Fonte única: enumerações do XSD oficial
/// (tiposSimples_v1.01.xsd, esquemas de Produção Restrita de 20/10/2026),
/// compartilhadas por CadastrarEmpresaRequestValidator e
/// AtualizarEmpresaRequestValidator. Os TEXTOS de cada código ficam na
/// tela (Views/Empresas/Index.cshtml) e no DANFSe (CodigosNfse) — se o XSD
/// mudar, os três lugares mudam juntos.
/// </summary>
internal static class CodigosFiscaisEmpresa
{
    /// <summary>TSOpSimpNac: 1 Não optante, 2 MEI, 3 ME/EPP.</summary>
    public static readonly string[] OpcoesSimplesNacional = { "1", "2", "3" };

    /// <summary>TSRegEspTrib.</summary>
    public static readonly string[] RegimesEspeciais = { "0", "1", "2", "3", "4", "5", "6", "9" };

    /// <summary>TSTribISSQN: 1 Tributável, 2 Imunidade, 3 Exportação, 4 Não incidência.</summary>
    public static readonly string[] TributacoesIssqn = { "1", "2", "3", "4" };

    /// <summary>TSTipoRetISSQN: 1 Não retido, 2 Retido pelo tomador, 3 Retido pelo intermediário.</summary>
    public static readonly string[] RetencoesIssqn = { "1", "2", "3" };

    /// <summary>
    /// TSTipoCST — só os códigos de SAÍDA (00–09, 49, 99). O XSD também
    /// aceita 50–75 e 98, mas esses são de operações de entrada/aquisição
    /// (crédito de PIS/COFINS), que não fazem sentido numa NFS-e emitida
    /// pelo prestador; deixá-los na tela só abriria espaço pra erro.
    /// </summary>
    public static readonly string[] CstsPisCofins = { "00", "01", "02", "03", "04", "05", "06", "07", "08", "09", "49", "99" };

    /// <summary>TSTipoRetPISCofins: 0–9.</summary>
    public static readonly string[] RetencoesPisCofins = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
}
