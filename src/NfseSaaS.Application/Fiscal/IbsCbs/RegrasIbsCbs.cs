namespace NfseSaaS.Application.Fiscal.IbsCbs;

/// <summary>O que vai no grupo IBSCBS da DPS para uma nota.</summary>
/// <param name="CIndOp">Indicador da operação; nulo quando o CST não tem tributação (E0859).</param>
/// <param name="ComTributacao">ind_gIBSCBS = 1: vai o cIndOp e o grupo gIBSCBS (E0860/E0960).</param>
/// <param name="Sugerida">Veio da sugestão do Anexo VIII (o Serviço não tinha a classificação preenchida).</param>
public sealed record ClassificacaoIbsCbs(string? CIndOp, string Cst, string CClassTrib, bool ComTributacao, bool Sugerida);

/// <summary>Resultado de <see cref="RegrasIbsCbs.Resolver"/>: a classificação OU o motivo de não ter uma.</summary>
public sealed record ResolucaoIbsCbs(ClassificacaoIbsCbs? Classificacao, string? Problema);

/// <summary>
/// Regras do IBS/CBS na DPS (F1, NT 009 e Anexo VI v1.04.01), sem banco
/// nem SEFIN — testáveis isoladas:
/// - na DPS só vai a CLASSIFICAÇÃO (cIndOp, indDest, CST, cClassTrib); os
///   valores e alíquotas a SEFIN calcula e devolve na NFS-e;
/// - CST = 3 primeiros dígitos do cClassTrib (E0959);
/// - CST 400 (isenção), 410 (imunidade/não incidência) e 820 (documento
///   específico) não têm tributação a declarar (ind_gIBSCBS = 0): sem
///   cIndOp e sem gIBSCBS (E0859/E0961). Os demais têm: com cIndOp e com
///   gIBSCBS (E0860/E0960);
/// - o Serviço pode fixar cIndOp e cClassTrib; vazio, vale a primeira
///   opção do Anexo VIII para o item da LC 116 (cIndOp só entre os que o
///   sistema consegue emitir, ver IndicadorOperacao.SuportadoPeloSistema);
/// - quando informar: não optante a partir da competência 01/01/2026
///   (E0850; obrigatório desde 01/10/2026, E0855); Simples Nacional (MEI e
///   ME/EPP) a partir da data configurada (padrão 01/01/2027, NT 009) e,
///   antes disso, só em homologação, para testar.
/// </summary>
public static class RegrasIbsCbs
{
    /// <summary>Primeira competência aceita com IBS/CBS (E0850).</summary>
    public static readonly DateOnly InicioIbsCbs = new(2026, 1, 1);

    /// <summary>Padrão: Simples Nacional obrigado "a partir de 2027" (NT 009).</summary>
    public static readonly DateOnly InicioSimplesNacionalPadrao = new(2027, 1, 1);

    /// <summary>CSTs sem tributação a declarar (ind_gIBSCBS = 0, NT 009 item 2.16).</summary>
    public static readonly IReadOnlySet<string> CstsSemTributacao = new HashSet<string> { "400", "410", "820" };

    public static string CstDe(string cClassTrib) => cClassTrib[..3];

    public static bool TemTributacao(string cst) => !CstsSemTributacao.Contains(cst);

    /// <summary>A nota leva o grupo IBSCBS?</summary>
    /// <param name="opSimpNac">"1" não optante, "2" MEI, "3" ME/EPP.</param>
    public static bool DeveInformar(string opSimpNac, DateOnly competencia, bool producao, DateOnly inicioSimplesNacional, bool simplesNacionalEmHomologacao)
    {
        if (competencia < InicioIbsCbs)
            return false;
        if (opSimpNac is not ("2" or "3"))
            return true;
        return competencia >= inicioSimplesNacional || (!producao && simplesNacionalEmHomologacao);
    }

    /// <summary>Classificação da nota a partir do Serviço (com a sugestão do Anexo VIII no que estiver vazio).</summary>
    public static ResolucaoIbsCbs Resolver(string codigoTributacaoNacional, string? cIndOpDoServico, string? cClassTribDoServico)
    {
        var opcoes = CatalogoIbsCbs.OpcoesDoServico(codigoTributacaoNacional);
        var sugerida = string.IsNullOrWhiteSpace(cClassTribDoServico) || string.IsNullOrWhiteSpace(cIndOpDoServico);

        var cClassTrib = CatalogoIbsCbs.Digitos(cClassTribDoServico) ?? opcoes?.Classificacoes.FirstOrDefault()?.Codigo;
        if (cClassTrib is not { Length: 6 })
            return new ResolucaoIbsCbs(null,
                "O serviço não tem a classificação do IBS/CBS (cClassTrib) e a tabela oficial não traz sugestão para esse código de tributação. Preencha a classificação em Serviços.");

        var cst = CstDe(cClassTrib);
        if (!TemTributacao(cst))
            return new ResolucaoIbsCbs(new ClassificacaoIbsCbs(null, cst, cClassTrib, false, sugerida), null);

        var cIndOp = CatalogoIbsCbs.Digitos(cIndOpDoServico)
            ?? opcoes?.IndicadoresOperacao.FirstOrDefault(i => i.SuportadoPeloSistema)?.Codigo;
        if (cIndOp is null)
            return new ResolucaoIbsCbs(null,
                "O indicador da operação do IBS/CBS (cIndOp) que a tabela oficial prevê para esse serviço pede dados que o micoempresa ainda não envia (imóvel, evento ou destinatário diferente do tomador). Por enquanto, emita esse serviço pelo Emissor Nacional (gov.br).");

        if (CatalogoIbsCbs.IndicadorOperacao(cIndOp) is not { SuportadoPeloSistema: true })
            return new ResolucaoIbsCbs(null,
                $"O indicador da operação {cIndOp} não pode ser usado pelo micoempresa (exige dados de imóvel, evento ou destinatário, ou não vale para NFS-e). Escolha outro em Serviços.");

        return new ResolucaoIbsCbs(new ClassificacaoIbsCbs(cIndOp, cst, cClassTrib, true, sugerida), null);
    }

    /// <summary>Mensagem do cadastro do Serviço para um cIndOp informado (nulo = aceito).</summary>
    public static string? ProblemaIndicador(string? cIndOp)
    {
        if (string.IsNullOrWhiteSpace(cIndOp))
            return null;
        return CatalogoIbsCbs.IndicadorOperacao(cIndOp) switch
        {
            null => "Indicador da operação (cIndOp) não encontrado na tabela oficial (Anexo VII).",
            { Nfse: false } => "Esse indicador da operação não vale para NFS-e.",
            { SuportadoPeloSistema: false } => "Esse indicador da operação pede dados que o micoempresa ainda não envia (imóvel, evento ou destinatário diferente do tomador).",
            _ => null
        };
    }

    /// <summary>Mensagem do cadastro do Serviço para o par cClassTrib + cIndOp (nulo = aceito).</summary>
    public static string? ProblemaClassificacao(string? cClassTrib, string? cIndOp)
    {
        if (string.IsNullOrWhiteSpace(cClassTrib))
            return null;
        if (CatalogoIbsCbs.Classificacao(cClassTrib) is not { } classificacao)
            return "Classificação tributária (cClassTrib) não encontrada na tabela oficial de serviços.";
        if (!TemTributacao(classificacao.Cst) && !string.IsNullOrWhiteSpace(cIndOp))
            return $"Com o CST {classificacao.Cst} (sem tributação a declarar) não se informa o indicador da operação: deixe-o em branco.";
        return null;
    }
}
