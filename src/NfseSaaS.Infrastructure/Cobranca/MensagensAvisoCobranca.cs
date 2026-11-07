using System.Globalization;
using System.Net;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Cobranca;

namespace NfseSaaS.Infrastructure.Cobranca;

/// <summary>
/// Assunto e corpo HTML de cada aviso de cobrança. Separado do serviço para
/// ser testado sem banco. Todo texto vindo de dados (plano, link) é
/// codificado; o link da fatura é o do Asaas (Pix, boleto ou cartão).
/// </summary>
public static class MensagensAvisoCobranca
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private const string Homologacao =
        "As notas em homologação, as consultas e os downloads continuam liberados.";

    /// <param name="linkAssinatura">Endereço da tela Assinatura (nulo sem Email:AppBaseUrl).</param>
    /// <param name="contatoSuporte">"suporte@... ou WhatsApp ..." (SuporteOptions.TextoContato); vazio = sem a linha.</param>
    public static (string Assunto, string CorpoHtml) Montar(
        AvisoCobranca aviso, EstadoAvisoCobranca estado, string? planoNome, string? linkAssinatura, string? contatoSuporte = null)
    {
        var data = aviso.Referencia is { } r ? r.ToString("dd/MM/yyyy", PtBr) : string.Empty;
        var valor = estado.ValorFaturaPendente is { } v ? $" de <strong>{Html(v.ToString("C", PtBr))}</strong>" : string.Empty;
        var plano = string.IsNullOrWhiteSpace(planoNome) ? string.Empty : $" (plano {Html(planoNome)})";
        var urlFatura = estado.UrlFaturaPendente;

        var (assunto, paragrafo, botao) = aviso.Tipo switch
        {
            TipoAvisoCobranca.TesteAcabandoDias => (
                aviso.Quantidade == 1 ? "O seu teste grátis termina amanhã" : $"O seu teste grátis termina em {aviso.Quantidade} dias",
                $"O teste grátis em produção desta conta termina em <strong>{data}</strong>. Para não parar de emitir, escolha um plano em <em>Assinatura</em>: dá para assinar agora, e a primeira cobrança só vence no fim do teste."
                + (estado.FundadorReservado ? " <strong>O seu preço de fundador está garantido</strong>: veja o prazo em <em>Assinatura</em>." : string.Empty),
                Botao.Assinatura),

            TipoAvisoCobranca.TesteAcabandoNotas => (
                aviso.Quantidade == 1 ? "Resta 1 nota no seu teste grátis" : $"Restam {aviso.Quantidade} notas no seu teste grátis",
                "O teste grátis desta conta é contado em dias, sem limite de notas. Veja a data em <em>Assinatura</em>.",
                Botao.Assinatura),

            TipoAvisoCobranca.FaturaCriada => (
                $"Sua fatura do micoempresa vence em {data}",
                $"A fatura da sua assinatura{plano}{valor} já está disponível, com vencimento em <strong>{data}</strong>. Você pode pagar por Pix, boleto ou cartão.",
                Botao.Fatura),

            TipoAvisoCobranca.FaturaVenceAmanha => (
                "Sua fatura do micoempresa vence amanhã",
                $"A fatura da sua assinatura{plano}{valor} vence <strong>amanhã ({data})</strong>. Se já pagou, desconsidere este aviso.",
                Botao.Fatura),

            TipoAvisoCobranca.FaturaVencida => (
                "Fatura do micoempresa vencida",
                $"A fatura da sua assinatura{plano}{valor}, vencida em <strong>{data}</strong>, ainda não consta como paga. A emissão em produção continua funcionando até <strong>{aviso.Referencia!.Value.AddDays(Planos.ToleranciaAtrasoDias).ToString("dd/MM/yyyy", PtBr)}</strong>; depois disso para até o pagamento ser confirmado. Se já pagou, desconsidere: a confirmação pode levar até 3 dias úteis no boleto.",
                Botao.Fatura),

            TipoAvisoCobranca.EmissaoBloqueada => (
                "Emissão em produção bloqueada no micoempresa",
                (estado.Status == Domain.Enums.StatusAssinatura.Atrasada
                    ? "A fatura da sua assinatura não foi paga dentro da tolerância: <strong>a emissão de notas em produção está bloqueada</strong>, inclusive a nota mensal automática. A liberação é automática assim que o pagamento for confirmado."
                    : estado.PrimeiraCobrancaEm is not null
                        ? "A primeira fatura da sua assinatura não foi paga dentro da tolerância: <strong>a emissão de notas em produção está bloqueada</strong>, inclusive a nota mensal automática. A liberação é automática assim que o pagamento for confirmado."
                        : $"O teste grátis de {Planos.TesteDias} dias terminou: <strong>a emissão de notas em produção está bloqueada</strong>, inclusive a nota mensal automática. Assine um plano em <em>Assinatura</em> para voltar a emitir.")
                + " " + Homologacao,
                urlFatura is null ? Botao.Assinatura : Botao.Fatura),

            TipoAvisoCobranca.AssinaturaCancelada => (
                "Sua assinatura do micoempresa foi cancelada",
                estado.AcessoAteAposCancelar is { } ate
                    ? $"A assinatura desta conta{plano} foi cancelada em <strong>{data}</strong>. Não haverá novas faturas, e a emissão em produção continua até <strong>{ate.ToString("dd/MM/yyyy", PtBr)}</strong>; depois disso para, inclusive a nota mensal automática. {Homologacao} Para continuar, assine de novo em <em>Assinatura</em> (o preço de fundador não volta)."
                    : $"A assinatura desta conta{plano} foi cancelada em <strong>{data}</strong>. A partir de agora <strong>a emissão em produção está bloqueada</strong>, inclusive a nota mensal automática, e não haverá novas faturas. {Homologacao} Para voltar a emitir, assine de novo em <em>Assinatura</em>.",
                Botao.Assinatura),

            TipoAvisoCobranca.PagamentoConfirmado => (
                "Pagamento confirmado — obrigado!",
                $"Recebemos em <strong>{data}</strong> o pagamento da sua assinatura. A emissão em produção está liberada. Obrigado por usar o micoempresa!",
                Botao.Assinatura),

            TipoAvisoCobranca.LimiteNotas => (
                aviso.Quantidade >= estado.Uso?.NotasPorMes
                    ? $"Limite de notas do plano {estado.Uso?.PlanoNome} atingido neste mês"
                    : $"Você já emitiu {aviso.Quantidade} de {estado.Uso?.NotasPorMes} notas do plano {estado.Uso?.PlanoNome} neste mês",
                $"Este mês a conta já emitiu <strong>{aviso.Quantidade} de {estado.Uso?.NotasPorMes} notas</strong> em produção, o limite do plano {Html(estado.Uso?.PlanoNome ?? "")}. <strong>Nada é bloqueado</strong>: a emissão continua normalmente. Se esse volume for o normal da sua conta, troque para um plano maior em <em>Assinatura</em>; o novo valor vale a partir da próxima fatura.",
                Botao.Trocar),

            TipoAvisoCobranca.LimiteEmpresas => (
                $"Sua conta passou do limite de empresas do plano {estado.Uso?.PlanoNome}",
                $"A conta tem <strong>{aviso.Quantidade} empresas ativas</strong> e o plano {Html(estado.Uso?.PlanoNome ?? "")} permite até {estado.Uso?.MaxEmpresas}. <strong>Nada é bloqueado</strong>. Para regularizar, troque para um plano maior em <em>Assinatura</em> ou desative em <em>Empresas</em> as que não usa.",
                Botao.Trocar),

            _ => throw new ArgumentOutOfRangeException(nameof(aviso), aviso.Tipo, null)
        };

        var acao = botao switch
        {
            Botao.Fatura when urlFatura is not null =>
                $"""<p><a href="{Html(urlFatura)}" style="display:inline-block;padding:10px 18px;background:#16a34a;color:#fff;border-radius:6px;text-decoration:none;font-weight:600">Pagar a fatura</a></p>""",
            _ when linkAssinatura is not null =>
                $"""<p><a href="{Html(linkAssinatura)}" style="display:inline-block;padding:10px 18px;background:#2563eb;color:#fff;border-radius:6px;text-decoration:none;font-weight:600">{(botao == Botao.Trocar ? "Trocar de plano" : "Abrir Assinatura")}</a></p>""",
            _ => string.Empty
        };

        var suporte = string.IsNullOrWhiteSpace(contatoSuporte)
            ? string.Empty
            : $"<p>Dúvidas sobre o plano ou a fatura? Fale com o suporte: {Html(contatoSuporte)}.</p>";

        var corpoHtml = $"""
            <p>{paragrafo}</p>
            {acao}
            {suporte}
            <p style="color:#64748b;font-size:12px">Aviso automático da plataforma micoempresa. Você recebe por ser administrador da conta.</p>
            """;

        return (assunto, corpoHtml);
    }

    private enum Botao
    {
        Assinatura,
        Fatura,
        Trocar
    }

    private static string Html(string texto) => WebUtility.HtmlEncode(texto);
}
