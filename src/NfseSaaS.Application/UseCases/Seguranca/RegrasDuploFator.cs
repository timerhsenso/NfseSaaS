using System.Net;
using NfseSaaS.Domain.Legal;

namespace NfseSaaS.Application.UseCases.Seguranca;

/// <summary>
/// Verificação em duas etapas por e-mail (B3), opcional e por usuário:
/// depois da senha certa, o login pede um código de 6 dígitos enviado ao
/// e-mail do usuário. Usa o 2FA do próprio ASP.NET Core Identity
/// (TwoFactorEnabled + provedor "Email"), sem tabela nova.
/// - ligar: código mandado ao e-mail (prova que o e-mail recebe) e digitado;
/// - desligar: pede a senha;
/// - "não pedir de novo neste navegador": cookie do Identity por
///   <see cref="DiasLembrarNavegador"/> dias;
/// - código errado conta como tentativa errada de login (5 → bloqueio de 15 min);
/// - "esqueci a senha" continua entrando direto depois de trocar a senha:
///   o link chega pelo mesmo e-mail que receberia o código (mesmo fator).
/// </summary>
public static class RegrasDuploFator
{
    /// <summary>Provedor de token do Identity (TokenOptions.DefaultEmailProvider).</summary>
    public const string Provedor = "Email";

    /// <summary>Finalidade do código que liga a verificação (diferente do código do login).</summary>
    public const string PropositoAtivacao = "AtivarDuploFator";

    /// <summary>Tempo para digitar o código do login (é o que dura o cookie da etapa 2 do Identity).</summary>
    public const int MinutosParaDigitar = 5;

    public const int DiasLembrarNavegador = 14;

    /// <summary>Prazo para o SMTP aceitar o e-mail do código (a pessoa espera na tela).</summary>
    public const int SegundosParaEnviar = 20;

    public const string MensagemCodigoInvalido = "Código inválido ou vencido. Confira o último e-mail ou peça um novo.";
    public const string MensagemSessaoExpirou = "O tempo para digitar o código acabou. Entre de novo com e-mail e senha.";
    public const string MensagemEnvioFalhou = "Não conseguimos enviar o código agora. Tente de novo em instantes.";
    public const string MensagemEmailNaoConfirmado = "Confirme o seu e-mail antes de ligar a verificação em duas etapas.";

    /// <summary>"123 456", "123-456" → "123456" (o usuário cola do e-mail do jeito que vier).</summary>
    public static string NormalizarCodigo(string? codigo) =>
        new string((codigo ?? string.Empty).Where(char.IsDigit).ToArray());

    public static string AssuntoLogin(string codigo) => $"{codigo} é o seu código de acesso ao {DocumentosLegais.NomeServico}";

    public static string AssuntoAtivacao(string codigo) => $"{codigo} é o código para ligar a verificação em duas etapas";

    /// <summary>Corpo do e-mail com o código (vai direto pelo SMTP: não fica no log de e-mails).</summary>
    public static string Corpo(string codigo, bool ativacao)
    {
        var finalidade = ativacao
            ? "para <strong>ligar a verificação em duas etapas</strong> na sua conta"
            : "para <strong>entrar</strong> na sua conta";
        return $"""
            <p>Olá,</p>
            <p>Use o código abaixo {finalidade} do {DocumentosLegais.NomeServico}:</p>
            <p style="font-size:28px;font-weight:bold;letter-spacing:6px">{WebUtility.HtmlEncode(codigo)}</p>
            <p>Ele vale por poucos minutos. Não passe este código para ninguém: a equipe do {DocumentosLegais.NomeServico} nunca pede.</p>
            <p style="color:#64748b;font-size:12px">Se não foi você, troque a sua senha: alguém pode estar tentando entrar na sua conta.</p>
            """;
    }

    public static string CorpoAviso(bool ligou) => ligou
        ? $"""
            <p>A <strong>verificação em duas etapas</strong> foi ligada na sua conta do {DocumentosLegais.NomeServico}.</p>
            <p>A partir de agora, depois da senha, o login pede um código enviado para este e-mail.</p>
            <p style="color:#64748b;font-size:12px">Se não foi você, troque a sua senha e fale com o administrador da sua conta.</p>
            """
        : $"""
            <p>A <strong>verificação em duas etapas</strong> foi desligada na sua conta do {DocumentosLegais.NomeServico}.</p>
            <p>O login volta a pedir só e-mail e senha.</p>
            <p style="color:#64748b;font-size:12px">Se não foi você, troque a sua senha agora e fale com o administrador da sua conta.</p>
            """;
}
