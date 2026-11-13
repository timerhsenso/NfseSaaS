using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Application.UseCases.EmailNota;

/// <summary>Um campo que o usuário insere no texto (botão) e o sistema troca pelo dado da nota.</summary>
/// <param name="Etiqueta">Como aparece no texto, ex.: "[Número da nota]".</param>
/// <param name="Exemplo">Valor de exemplo para a prévia.</param>
public sealed record CampoModeloEmailNota(string Etiqueta, string Descricao, string Exemplo);

/// <summary>Dados de uma nota para preencher o e-mail.</summary>
public sealed record DadosEmailNota(
    string Cliente,
    string Empresa,
    string EmpresaRazaoSocial,
    string? EmpresaEmail,
    string Numero,
    DateOnly Competencia,
    decimal Valor,
    DateTimeOffset? Emissao,
    string Servico,
    string? NumeroContrato,
    string? ChaveAcesso,
    bool Homologacao);

/// <summary>
/// Modelo em vigor para uma nota (o do cliente, o da empresa, o padrão da
/// conta ou o texto padrão do sistema). ModeloId nulo = texto do sistema.
/// </summary>
public sealed record ModeloEmailNotaEfetivo(
    Guid? ModeloId,
    string Nome,
    string Assunto,
    string Mensagem,
    string? ResponderPara,
    string? CopiaOcultaPara,
    bool OcultarDivulgacao);

/// <summary>
/// Texto do e-mail da nota (cadastro Modelos de e-mail): campos entre colchetes que o usuário insere
/// por botões ("[Número da nota]") e o sistema troca pelos dados de cada
/// nota. Puro — a prévia da tela (modelos-email.js) repete a mesma
/// troca. O que dá validade à nota (quadro com número, valor e chave,
/// consulta pública, anexos) fica fixo abaixo da mensagem: o usuário
/// personaliza a conversa, não tira o essencial.
/// </summary>
public static partial class RegrasModeloEmailNota
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly TimeSpan OffsetBrasilia = TimeSpan.FromHours(-3);

    public const string CampoCliente = "[Cliente]";
    public const string CampoEmpresa = "[Empresa]";
    public const string CampoNumero = "[Número da nota]";
    public const string CampoMesExtenso = "[Mês por extenso]";
    public const string CampoCompetencia = "[Competência]";
    public const string CampoValor = "[Valor]";
    public const string CampoEmissao = "[Data de emissão]";
    public const string CampoServico = "[Serviço]";
    public const string CampoContrato = "[Nº do contrato]";

    public static readonly IReadOnlyList<CampoModeloEmailNota> Campos = new[]
    {
        new CampoModeloEmailNota(CampoCliente, "Nome do cliente (tomador)", "Padaria Sol Nascente"),
        new CampoModeloEmailNota(CampoNumero, "Número da NFS-e", "58"),
        new CampoModeloEmailNota(CampoMesExtenso, "Mês da competência por extenso", "novembro de 2026"),
        new CampoModeloEmailNota(CampoCompetencia, "Competência em números", "11/2026"),
        new CampoModeloEmailNota(CampoValor, "Valor do serviço", "R$ 344,21"),
        new CampoModeloEmailNota(CampoEmissao, "Data de emissão", "12/11/2026"),
        new CampoModeloEmailNota(CampoServico, "Descrição do serviço (resumida)", "Manutenção do sistema"),
        new CampoModeloEmailNota(CampoContrato, "Número do contrato (vazio na nota avulsa)", "2026/0001"),
        new CampoModeloEmailNota(CampoEmpresa, "Nome da sua empresa (emitente)", "Sua Empresa")
    };

    public const string AssuntoPadrao = "Nota fiscal " + CampoNumero + " — " + CampoEmpresa;

    public const string MensagemPadrao =
        "Olá, " + CampoCliente + ".\n\n" +
        "Segue a nota fiscal de serviço nº " + CampoNumero + ", referente a " + CampoMesExtenso + ", no valor de " + CampoValor + ". " +
        "O PDF e o XML estão anexados.\n\n" +
        "Qualquer dúvida, é só responder este e-mail.\n\n" +
        "Atenciosamente,\n" + CampoEmpresa;

    /// <summary>Nome do modelo que toda conta ganha (com o texto padrão).</summary>
    public const string NomeModeloInicial = "Padrão";

    /// <summary>Limite de modelos por conta (cadastro, não é cobrança).</summary>
    public const int MaximoModelos = 30;

    public static ModeloEmailNotaEfetivo Padrao { get; } = new(null, "Texto padrão do sistema", AssuntoPadrao, MensagemPadrao, null, null, false);

    /// <summary>
    /// O modelo que vale para uma nota: o do cliente, senão o da empresa,
    /// senão o padrão da conta, senão o texto do sistema. Tirar o rodapé só
    /// vale no plano Escritório.
    /// </summary>
    public static ModeloEmailNotaEfetivo Resolver(
        ModeloEmailNota? doCliente, ModeloEmailNota? daEmpresa, ModeloEmailNota? padraoDaConta, bool podeOcultarDivulgacao) =>
        Efetivo(doCliente ?? daEmpresa ?? padraoDaConta, podeOcultarDivulgacao);

    public static ModeloEmailNotaEfetivo Efetivo(ModeloEmailNota? modelo, bool podeOcultarDivulgacao) =>
        modelo is null
            ? Padrao
            : new ModeloEmailNotaEfetivo(modelo.Id, modelo.Nome, modelo.Assunto, modelo.Mensagem, modelo.ResponderPara, modelo.CopiaOcultaPara,
                podeOcultarDivulgacao && modelo.OcultarDivulgacao);

    /// <summary>"novembro de 2026".</summary>
    public static string MesPorExtenso(DateOnly competencia) =>
        $"{PtBr.DateTimeFormat.GetMonthName(competencia.Month)} de {competencia.Year}";

    /// <summary>Valor de cada campo para esta nota.</summary>
    public static IReadOnlyDictionary<string, string> Valores(DadosEmailNota d) => new Dictionary<string, string>
    {
        [CampoCliente] = d.Cliente,
        [CampoEmpresa] = d.Empresa,
        [CampoNumero] = d.Numero,
        [CampoMesExtenso] = MesPorExtenso(d.Competencia),
        [CampoCompetencia] = d.Competencia.ToString("MM/yyyy", PtBr),
        [CampoValor] = d.Valor.ToString("C", PtBr),
        [CampoEmissao] = d.Emissao?.ToOffset(OffsetBrasilia).ToString("dd/MM/yyyy", PtBr) ?? "-",
        [CampoServico] = Resumir(d.Servico, 120),
        [CampoContrato] = d.NumeroContrato ?? string.Empty
    };

    /// <summary>Troca os campos (sem diferença de maiúsculas). Colchete que não é campo fica como está.</summary>
    public static string Preencher(string texto, IReadOnlyDictionary<string, string> valores) =>
        RegexCampo().Replace(texto ?? string.Empty, m =>
            valores.FirstOrDefault(v => string.Equals(v.Key, m.Value, StringComparison.OrdinalIgnoreCase)).Value ?? m.Value);

    /// <summary>Assunto pronto: campos trocados, uma linha só, no tamanho do e-mail; [TESTE] na homologação.</summary>
    public static string MontarAssunto(string modelo, DadosEmailNota dados)
    {
        var assunto = Regex.Replace(Preencher(modelo, Valores(dados)), @"\s+", " ").Trim();
        if (assunto.Length == 0)
            assunto = Preencher(AssuntoPadrao, Valores(dados));
        if (assunto.Length > 200)
            assunto = assunto[..200];
        return (dados.Homologacao ? "[TESTE] " : string.Empty) + assunto;
    }

    /// <summary>
    /// Corpo HTML: a mensagem do usuário (texto simples, escapado, quebras de
    /// linha viram parágrafos) e, abaixo, o que é fixo: aviso de homologação,
    /// quadro da nota, consulta pública e rodapé.
    /// </summary>
    public static string MontarCorpoHtml(string mensagem, DadosEmailNota d, bool mostrarDivulgacao, string linkDivulgacao)
    {
        var texto = Preencher(mensagem, Valores(d)).Replace("\r\n", "\n").Trim();
        if (texto.Length == 0)
            texto = Preencher(MensagemPadrao, Valores(d));

        var html = new StringBuilder();
        foreach (var paragrafo in Regex.Split(texto, @"\n\s*\n"))
        {
            var linhas = paragrafo.Split('\n').Select(l => WebUtility.HtmlEncode(l.TrimEnd()));
            html.Append("<p>").Append(string.Join("<br>", linhas)).Append("</p>");
        }

        if (d.Homologacao)
            html.Append("<p><strong>Atenção: esta nota foi emitida no ambiente de TESTES da Receita e não tem valor fiscal.</strong></p>");

        var valores = Valores(d);
        html.Append("<table cellpadding=\"4\" style=\"border-collapse:collapse;border-top:1px solid #e2e8f0;margin-top:8px\">");
        Linha(html, "Número", $"<strong>{WebUtility.HtmlEncode(d.Numero)}</strong>");
        Linha(html, "Emissão", valores[CampoEmissao]);
        Linha(html, "Competência", valores[CampoCompetencia]);
        Linha(html, "Valor do serviço", valores[CampoValor]);
        Linha(html, "Emitente", WebUtility.HtmlEncode(d.EmpresaRazaoSocial));
        if (!string.IsNullOrWhiteSpace(d.ChaveAcesso))
            Linha(html, "Chave de acesso", WebUtility.HtmlEncode(d.ChaveAcesso));
        html.Append("</table>");

        if (!string.IsNullOrWhiteSpace(d.ChaveAcesso))
        {
            var link = $"https://www.nfse.gov.br/ConsultaPublica/?tpc=1&chave={Uri.EscapeDataString(d.ChaveAcesso)}";
            html.Append($"<p style=\"font-size:13px\">Confira a autenticidade da nota na <a href=\"{WebUtility.HtmlEncode(link)}\">consulta pública da NFS-e</a>.</p>");
        }

        html.Append($"<p style=\"color:#64748b;font-size:12px\">Enviado pela plataforma micoempresa em nome de {WebUtility.HtmlEncode(d.Empresa)}.</p>");
        if (mostrarDivulgacao)
            html.Append($"<p style=\"color:#64748b;font-size:12px\">Você também presta serviço? Emita suas notas com o <a href=\"{WebUtility.HtmlEncode(linkDivulgacao)}\" style=\"color:#2563eb\">micoempresa</a>: nota mensal automática e nota enviada por e-mail ao seu cliente. Crie sua conta grátis.</p>");

        return html.ToString();
    }

    /// <summary>Erros do modelo inteiro (nome incluído); vazio = válido.</summary>
    public static IReadOnlyList<string> ValidarModelo(string? nome, string? assunto, string? mensagem, string? responderPara, string? copiaOcultaPara)
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(nome))
            erros.Add("Dê um nome ao modelo (ex.: \"Padrão escritório\").");
        else if (nome.Trim().Length > ModeloEmailNota.TamanhoMaximoNome)
            erros.Add($"O nome pode ter no máximo {ModeloEmailNota.TamanhoMaximoNome} caracteres.");
        erros.AddRange(Validar(assunto, mensagem, responderPara, copiaOcultaPara));
        return erros;
    }

    /// <summary>Erros do texto (vazio = válido). E-mails opcionais, mas se informados precisam ser válidos.</summary>
    public static IReadOnlyList<string> Validar(string? assunto, string? mensagem, string? responderPara, string? copiaOcultaPara)
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(assunto))
            erros.Add("Informe o assunto do e-mail.");
        else if (assunto.Trim().Length > ModeloEmailNota.TamanhoMaximoAssunto)
            erros.Add($"O assunto pode ter no máximo {ModeloEmailNota.TamanhoMaximoAssunto} caracteres.");

        if (string.IsNullOrWhiteSpace(mensagem))
            erros.Add("Escreva a mensagem do e-mail.");
        else if (mensagem.Trim().Length > ModeloEmailNota.TamanhoMaximoMensagem)
            erros.Add($"A mensagem pode ter no máximo {ModeloEmailNota.TamanhoMaximoMensagem} caracteres.");

        if (!string.IsNullOrWhiteSpace(responderPara) && !EmailValido(responderPara))
            erros.Add("O e-mail de \"Responder para\" não é válido.");
        if (!string.IsNullOrWhiteSpace(copiaOcultaPara) && !EmailValido(copiaOcultaPara))
            erros.Add("O e-mail da cópia oculta não é válido.");
        return erros;
    }

    public static bool EmailValido(string email) =>
        email.Trim().Length <= ModeloEmailNota.TamanhoMaximoEmail && RegexEmail().IsMatch(email.Trim());

    /// <summary>Etiquetas entre colchetes que não são campos conhecidos (para avisar na tela).</summary>
    public static IReadOnlyList<string> CamposDesconhecidos(string texto) =>
        RegexCampo().Matches(texto ?? string.Empty)
            .Select(m => m.Value)
            .Where(v => !Campos.Any(c => string.Equals(c.Etiqueta, v, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void Linha(StringBuilder html, string rotulo, string valorHtml) =>
        html.Append($"<tr><td style=\"color:#64748b\">{rotulo}</td><td>{valorHtml}</td></tr>");

    private static string Resumir(string texto, int maximo)
    {
        var umaLinha = Regex.Replace(texto ?? string.Empty, @"\s+", " ").Trim();
        return umaLinha.Length <= maximo ? umaLinha : umaLinha[..maximo] + "…";
    }

    [GeneratedRegex(@"\[[^\[\]\r\n]{1,40}\]")]
    private static partial Regex RegexCampo();

    [GeneratedRegex(@"^[^@\s;,]+@[^@\s;,]+\.[^@\s;,]+$")]
    private static partial Regex RegexEmail();
}
