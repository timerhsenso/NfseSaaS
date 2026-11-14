using System.Text.RegularExpressions;

namespace NfseSaaS.Application.Common;

/// <summary>
/// SEG1 — o que nunca pode ir para o log (arquivo, console e e-mail de
/// alerta ao operador): senhas, tokens, chaves de API, cookies e o CPF/CNPJ
/// que aparece em respostas de APIs de terceiros (ex.: Asaas). Usado pelo
/// enricher do Serilog (Web/Monitoramento/MascaramentoLogEnricher), que
/// troca o valor da propriedade antes de qualquer saída gravar.
/// Os ids do sistema (EmpresaId, TenantId…) e o CNPJ da empresa emissora
/// continuam no log: sem eles não dá para investigar um problema.
/// </summary>
public static partial class MascaramentoLog
{
    public const string Mascara = "***";

    // Nomes de propriedade sensíveis (comparados sem maiúsculas, "_" e "-").
    private static readonly string[] TrechosSensiveis =
    [
        "senha", "password", "passwd", "secret", "segredo", "token", "apikey", "authorization",
        "cookie", "privatekey", "chaveprivada", "pfx"
    ];

    /// <summary>Propriedade cujo valor inteiro some do log ({Senha}, {AccessToken}, {ApiKey}…).</summary>
    public static bool NomeSensivel(string nome)
    {
        var normalizado = nome.Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        return TrechosSensiveis.Any(normalizado.Contains);
    }

    /// <summary>
    /// Esconde segredos dentro de um texto (corpo de resposta, URL, mensagem):
    /// pares "chave": "valor" / chave=valor com nome sensível, CPF/CNPJ em
    /// JSON, parâmetros de link assinado (?t=, ?c=, token, code) e "Bearer/Basic …".
    /// </summary>
    public static string MascararTexto(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
            return texto ?? string.Empty;

        var resultado = ParChaveValor().Replace(texto, m => m.Groups["prefixo"].Value + Mascara);
        resultado = ParametroDeLink().Replace(resultado, m => m.Groups["prefixo"].Value + Mascara);
        resultado = Bearer().Replace(resultado, m => m.Groups["prefixo"].Value + Mascara);
        return resultado;
    }

    [GeneratedRegex("""(?<prefixo>["']?(?:senha|password|passwd|secret|segredo|access[_-]?token|refresh[_-]?token|token|api[_-]?key|asaas-access-token|authorization|cpfCnpj|cpf_cnpj|cpf)["']?\s*[:=]\s*["']?)(?<valor>(?!(?:Bearer|Basic)\s)[^"'&,;\s}\]]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ParChaveValor();

    [GeneratedRegex("""(?<prefixo>[?&](?:t|c|token|code|codigo)=)(?<valor>[^&\s"'#]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ParametroDeLink();

    [GeneratedRegex("""(?<prefixo>\b(?:Bearer|Basic)\s+)(?<valor>[A-Za-z0-9\-._~+/]+=*)""", RegexOptions.IgnoreCase)]
    private static partial Regex Bearer();
}
