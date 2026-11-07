namespace NfseSaaS.Application.UseCases.Catalogos;

/// <summary>
/// Códigos de tributação nacional (cTribNac) que exigem na DPS um grupo de
/// informações que o micoempresa ainda NÃO envia. Sem o grupo, a SEFIN
/// rejeita a nota — então barramos antes, no cadastro do Serviço e na
/// emissão, explicando o porquê e o que fazer.
///
/// Fonte: Anexo I da SEFIN (DPS/NFS-e, v1.01 de 09/02/2026):
/// - E0370: grupo "obra" obrigatório para os subitens abaixo;
/// - E0390: grupo "atvEvento" obrigatório para todo o item 12.
/// Quando o produto passar a enviar esses grupos (roadmap), sai daqui.
/// </summary>
public static class CodigosTributacaoNaoSuportados
{
    /// <summary>Subitens da regra E0370 (dados da obra).</summary>
    public static readonly IReadOnlySet<string> ExigemObra = new HashSet<string>
    {
        "070201", "070202", "070401", "070501", "070502", "070601", "070602",
        "070701", "070801", "071701", "071901", "141403", "141404"
    };

    /// <summary>Item 12 da lista (E0390: dados da atividade/evento).</summary>
    public static bool ExigeEvento(string? codigo) =>
        Normalizar(codigo) is { Length: 6 } c && c.StartsWith("12", StringComparison.Ordinal);

    public static bool ExigeObra(string? codigo) => Normalizar(codigo) is { } c && ExigemObra.Contains(c);

    /// <summary>Por que o código não pode ser usado (nulo = pode).</summary>
    public static string? Motivo(string? codigo)
    {
        if (ExigeObra(codigo))
            return $"O código {Formatar(codigo!)} é de obra (construção civil e congêneres): a nota precisa levar os dados da obra (CNO ou endereço), que o micoempresa ainda não envia, e a SEFIN rejeitaria a nota (E0370). Por enquanto, emita esse serviço pelo Emissor Nacional (gov.br).";

        if (ExigeEvento(codigo))
            return $"O código {Formatar(codigo!)} é do item 12 (espetáculos, shows, feiras e eventos): a nota precisa levar os dados do evento (nome, datas e local), que o micoempresa ainda não envia, e a SEFIN rejeitaria a nota (E0390). Por enquanto, emita esse serviço pelo Emissor Nacional (gov.br).";

        return null;
    }

    /// <summary>Texto curto para a lista de códigos da tela.</summary>
    public static string? Aviso(string? codigo) =>
        ExigeObra(codigo) ? "não disponível: exige dados da obra"
        : ExigeEvento(codigo) ? "não disponível: exige dados do evento"
        : null;

    private static string? Normalizar(string? codigo) =>
        string.IsNullOrWhiteSpace(codigo) ? null : new string(codigo.Where(char.IsDigit).ToArray());

    private static string Formatar(string codigo)
    {
        var c = Normalizar(codigo) ?? codigo;
        return c.Length == 6 ? $"{c[..2]}.{c[2..4]}.{c[4..]}" : c;
    }
}
