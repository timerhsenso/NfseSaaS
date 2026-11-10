namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>Motivo oficial da substituição (cMotivo, TSCodJustSubst).</summary>
public sealed record MotivoSubstituicao(string Codigo, string Descricao);

/// <summary>Campo da nota que a SEFIN não deixa mudar na substituta, para o regime da empresa.</summary>
public enum CampoTravadoSubstituicao
{
    Cliente = 1,
    Competencia = 2,
    Valor = 3,
    Servico = 4,
    LocalPrestacao = 5
}

/// <summary>O que a substituta muda em relação à original (para conferir antes de enviar).</summary>
public sealed record ComparacaoSubstituicao(
    string DocumentoTomadorOriginal, string DocumentoTomadorNovo,
    DateOnly CompetenciaOriginal, DateOnly CompetenciaNova,
    decimal ValorOriginal, decimal ValorNovo,
    string CodigoTributacaoOriginal, string CodigoTributacaoNovo,
    string LocalOriginal, string LocalNovo);

/// <summary>
/// C7: regras da substituição de NFS-e (grupo subst da DPS, leiaute 1.01).
/// A SEFIN cancela a original sozinha (evento 105102) quando autoriza a
/// substituta. O que não pode mudar depende do regime da empresa
/// (opSimpNac), conforme as rejeições oficiais:
/// - MEI (2) e ME/EPP (3): tomador, competência e valor (E0061/E0063);
/// - não optante (1): competência, subitem (cTribNac) e local da prestação (E0060).
/// Conferido aqui antes de gastar o número da DPS.
/// </summary>
public static class RegrasSubstituicao
{
    public const string MotivoOutros = "99";
    public const int DescricaoMinima = 15;
    public const int DescricaoMaxima = 255;

    public static readonly IReadOnlyList<MotivoSubstituicao> Motivos = new[]
    {
        new MotivoSubstituicao("99", "Outros (descreva o motivo)"),
        new MotivoSubstituicao("01", "Desenquadramento do Simples Nacional"),
        new MotivoSubstituicao("02", "Enquadramento no Simples Nacional"),
        new MotivoSubstituicao("03", "Inclusão retroativa de imunidade/isenção"),
        new MotivoSubstituicao("04", "Exclusão retroativa de imunidade/isenção"),
        new MotivoSubstituicao("05", "Rejeição da NFS-e pelo tomador ou intermediário responsável pelo recolhimento")
    };

    public static IReadOnlyList<CampoTravadoSubstituicao> CamposTravados(string opSimpNac) => opSimpNac switch
    {
        "2" or "3" => new[] { CampoTravadoSubstituicao.Cliente, CampoTravadoSubstituicao.Competencia, CampoTravadoSubstituicao.Valor },
        _ => new[] { CampoTravadoSubstituicao.Competencia, CampoTravadoSubstituicao.Servico, CampoTravadoSubstituicao.LocalPrestacao }
    };

    /// <summary>Motivo e descrição (xMotivo). Nulo = ok.</summary>
    public static string? ProblemaNoMotivo(string? motivo, string? descricao)
    {
        if (string.IsNullOrWhiteSpace(motivo) || Motivos.All(m => m.Codigo != motivo))
            return "Escolha o motivo da substituição.";

        var texto = descricao?.Trim() ?? string.Empty;
        if (motivo == MotivoOutros && texto.Length == 0)
            return $"Com o motivo \"Outros\", descreva o motivo da substituição ({DescricaoMinima} a {DescricaoMaxima} caracteres).";
        if (texto.Length > 0 && (texto.Length < DescricaoMinima || texto.Length > DescricaoMaxima))
            return $"A descrição do motivo precisa ter de {DescricaoMinima} a {DescricaoMaxima} caracteres.";
        return null;
    }

    /// <summary>Campos travados que mudaram. Vazio = ok.</summary>
    public static IReadOnlyList<string> CamposAlteradosIndevidamente(string opSimpNac, ComparacaoSubstituicao c)
    {
        var problemas = new List<string>();
        foreach (var campo in CamposTravados(opSimpNac))
        {
            var mudou = campo switch
            {
                CampoTravadoSubstituicao.Cliente => c.DocumentoTomadorOriginal != c.DocumentoTomadorNovo,
                CampoTravadoSubstituicao.Competencia => c.CompetenciaOriginal != c.CompetenciaNova,
                CampoTravadoSubstituicao.Valor => c.ValorOriginal != c.ValorNovo,
                CampoTravadoSubstituicao.Servico => c.CodigoTributacaoOriginal != c.CodigoTributacaoNovo,
                CampoTravadoSubstituicao.LocalPrestacao => c.LocalOriginal != c.LocalNovo,
                _ => false
            };
            if (mudou)
                problemas.Add(Nome(campo));
        }
        return problemas;
    }

    public static string MensagemCamposTravados(string opSimpNac, IReadOnlyList<string> alterados)
    {
        var regime = opSimpNac is "2" or "3" ? "do Simples Nacional" : "não optante pelo Simples";
        return $"Na substituição, empresa {regime} não pode mudar: {string.Join(", ", alterados)}. " +
               "Para mudar isso, cancele a nota e emita outra.";
    }

    public static string Nome(CampoTravadoSubstituicao campo) => campo switch
    {
        CampoTravadoSubstituicao.Cliente => "cliente (tomador)",
        CampoTravadoSubstituicao.Competencia => "competência",
        CampoTravadoSubstituicao.Valor => "valor do serviço",
        CampoTravadoSubstituicao.Servico => "serviço (código de tributação)",
        CampoTravadoSubstituicao.LocalPrestacao => "local da prestação",
        _ => campo.ToString()
    };
}
