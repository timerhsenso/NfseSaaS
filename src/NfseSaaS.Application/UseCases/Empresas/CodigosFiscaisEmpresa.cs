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

    /// <summary>
    /// TSTribISSQN aceito HOJE: só 1 (operação tributável). O XSD também
    /// tem 2 Imunidade, 3 Exportação e 4 Não incidência, mas cada um exige
    /// grupos que a DPS ainda não monta (tpImunidade, cPaisResult/comExt) —
    /// aceitá-los no cadastro fazia toda nota da Empresa ser rejeitada.
    /// Reabrir junto com o suporte a esses grupos.
    /// </summary>
    public static readonly string[] TributacoesIssqn = { "1" };

    /// <summary>
    /// Faixa da alíquota de ISS (%) enviada em pAliq. TSDec1V2 aceita até
    /// 9.99; o teto legal do ISS é 5% (LC 116, art. 8º). Piso não validado
    /// aqui de propósito (alíquota efetiva do Simples varia por faixa).
    /// </summary>
    public const decimal AliquotaIssMinima = 0.01m, AliquotaIssMaxima = 5.00m;

    /// <summary>
    /// TSTipoCST — só os códigos de SAÍDA (00–09, 49, 99). O XSD também
    /// aceita 50–75 e 98, mas esses são de operações de entrada/aquisição
    /// (crédito de PIS/COFINS), que não fazem sentido numa NFS-e emitida
    /// pelo prestador; deixá-los na tela só abriria espaço pra erro.
    /// </summary>
    public static readonly string[] CstsPisCofins = { "00", "01", "02", "03", "04", "05", "06", "07", "08", "09", "49", "99" };

    // tpRetPisCofins não é mais da Empresa: é calculado em cada nota pelo
    // que o tomador retém (C6, RegrasRetencoesFederais.TpRetPisCofins).
}
