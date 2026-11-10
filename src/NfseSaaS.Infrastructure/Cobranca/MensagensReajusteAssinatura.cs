using System.Net;
using NfseSaaS.Application.UseCases.Cobranca;

namespace NfseSaaS.Infrastructure.Cobranca;

/// <summary>
/// E-mail de aviso prévio do reajuste da assinatura (B2). Separado do
/// serviço para ser testado sem banco. Todo texto vindo de dados é codificado.
/// </summary>
public static class MensagensReajusteAssinatura
{
    /// <param name="linkAssinatura">Endereço da tela Assinatura (nulo sem Email:AppBaseUrl).</param>
    /// <param name="contatoSuporte">SuporteOptions.TextoContato; vazio = sem a linha.</param>
    public static (string Assunto, string CorpoHtml) Montar(
        string planoNome, decimal precoAnterior, decimal precoNovo, DateOnly aplicarEm, string? linkAssinatura, string? contatoSuporte = null)
    {
        var dia = RegrasReajusteAssinatura.Dia(aplicarEm);
        var assunto = $"Reajuste da sua assinatura do micoempresa a partir de {dia}";

        var botao = linkAssinatura is null
            ? string.Empty
            : $"""<p><a href="{Html(linkAssinatura)}" style="display:inline-block;padding:10px 18px;background:#2563eb;color:#fff;border-radius:6px;text-decoration:none;font-weight:600">Abrir Assinatura</a></p>""";

        var suporte = string.IsNullOrWhiteSpace(contatoSuporte)
            ? string.Empty
            : $"<p>Dúvidas? Fale com o suporte: {Html(contatoSuporte)}.</p>";

        var corpoHtml = $"""
            <p>Olá,</p>
            <p>O preço de tabela do plano <strong>{Html(planoNome)}</strong> mudou. A partir de <strong>{dia}</strong>, a mensalidade da sua assinatura passa de <strong>{Html(RegrasReajusteAssinatura.Reais(precoAnterior))}</strong> para <strong>{Html(RegrasReajusteAssinatura.Reais(precoNovo))}</strong>. As faturas com vencimento a partir dessa data já virão com o novo valor; as anteriores não mudam.</p>
            <p>Este aviso segue os Termos de uso (item 8.5): reajuste no máximo uma vez por ano, com pelo menos {RegrasReajusteAssinatura.AvisoMinimoDias} dias de antecedência. Se preferir, você pode trocar de plano ou cancelar pela tela <em>Assinatura</em>, sem multa.</p>
            {botao}
            {suporte}
            <p style="color:#64748b;font-size:12px">Aviso automático da plataforma micoempresa. Você recebe por ser administrador da conta.</p>
            """;

        return (assunto, corpoHtml);
    }

    private static string Html(string texto) => WebUtility.HtmlEncode(texto);
}
