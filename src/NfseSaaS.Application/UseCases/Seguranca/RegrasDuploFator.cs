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

    // ---------- Obrigatoriedade (SEG1) ----------

    /// <summary>
    /// Dia em que a verificação passa a ser exigida dos administradores. Quem
    /// já é administrador ganha <see cref="DiasPrazoAdesao"/> dias a partir
    /// daqui; quem entra depois, a partir do primeiro acesso.
    /// </summary>
    public static readonly DateOnly ObrigatoriaParaAdministradoresDesde = new(2026, 11, 16);

    /// <summary>Dias para ligar depois que a verificação passa a ser exigida (aviso no topo das telas até lá).</summary>
    public const int DiasPrazoAdesao = 14;

    public const string CodigoPendente = "duplo_fator_pendente";

    public const string CaminhoTelaObrigatoria = "/Seguranca/DuasEtapas";

    /// <summary>
    /// Quem precisa ligar a verificação e até quando:
    /// - operador da plataforma: sempre, sem prazo (mexe em todas as contas);
    /// - administrador da conta: a partir de <see cref="ObrigatoriaParaAdministradoresDesde"/>
    ///   ou do primeiro acesso, o que vier depois, mais <see cref="DiasPrazoAdesao"/> dias;
    /// - qualquer usuário, quando a conta exige de todos: a partir da data em
    ///   que o administrador ligou a exigência ou do primeiro acesso, mais o prazo.
    /// Com mais de um motivo, vale o prazo mais curto. Ligada = nada pendente.
    /// </summary>
    public static ExigenciaDuploFator Avaliar(
        bool ligado, bool operador, bool administrador, DateOnly? contaExigeDesde, DateOnly primeiroAcesso, DateOnly hoje)
    {
        var motivo = MotivoExigenciaDuploFator.Nenhum;
        DateOnly? prazo = null;
        var semPrazo = false;

        void Considerar(MotivoExigenciaDuploFator novoMotivo, DateOnly? novoPrazo)
        {
            if (semPrazo)
                return;
            if (novoPrazo is null)
            {
                semPrazo = true;
                prazo = null;
                motivo = novoMotivo;
                return;
            }
            if (prazo is null || novoPrazo < prazo)
            {
                prazo = novoPrazo;
                motivo = novoMotivo;
            }
        }

        if (operador)
            Considerar(MotivoExigenciaDuploFator.Operador, null);
        if (administrador)
            Considerar(MotivoExigenciaDuploFator.Administrador, Maior(ObrigatoriaParaAdministradoresDesde, primeiroAcesso).AddDays(DiasPrazoAdesao));
        if (contaExigeDesde is { } desde)
            Considerar(MotivoExigenciaDuploFator.Conta, Maior(desde, primeiroAcesso).AddDays(DiasPrazoAdesao));

        if (motivo == MotivoExigenciaDuploFator.Nenhum)
            return ExigenciaDuploFator.NaoExigida;

        var pendente = !ligado;
        var bloqueia = pendente && (prazo is null || hoje > prazo.Value);
        return new ExigenciaDuploFator(true, motivo, prazo, pendente, bloqueia);
    }

    /// <summary>Por que é exigida, no texto do aviso e da tela de ligar.</summary>
    public static string TextoMotivo(MotivoExigenciaDuploFator motivo) => motivo switch
    {
        MotivoExigenciaDuploFator.Operador => "para quem opera a plataforma",
        MotivoExigenciaDuploFator.Administrador => "para administradores da conta",
        MotivoExigenciaDuploFator.Conta => "para todos os usuários desta conta (decisão do administrador)",
        _ => string.Empty
    };

    /// <summary>Aviso no topo das telas enquanto corre o prazo.</summary>
    public static string TextoAvisoPrazo(ExigenciaDuploFator exigencia, DateOnly hoje)
    {
        if (exigencia.Prazo is not { } prazo)
            return string.Empty;
        var dias = prazo.DayNumber - hoje.DayNumber;
        var quando = dias switch
        {
            <= 0 => "até hoje",
            1 => "até amanhã",
            _ => $"até {prazo:dd/MM/yyyy} (faltam {dias} dias)"
        };
        var motivo = exigencia.Motivo == MotivoExigenciaDuploFator.Conta
            ? "O administrador da sua conta passou a exigir a verificação em duas etapas"
            : "A verificação em duas etapas passou a ser obrigatória " + TextoMotivo(exigencia.Motivo);
        return $"{motivo}. Ligue {quando}: depois disso, o sistema só abre com ela ligada.";
    }

    public const string MensagemBloqueio = "Para continuar, ligue a verificação em duas etapas.";

    public const string MensagemExigirSemTerLigado = "Ligue primeiro a sua verificação em duas etapas (menu do usuário) para poder exigir dos outros.";

    private static DateOnly Maior(DateOnly a, DateOnly b) => a > b ? a : b;
}

public enum MotivoExigenciaDuploFator
{
    Nenhum = 0,
    Operador = 1,
    Administrador = 2,
    Conta = 3
}

/// <param name="Exigida">Algum motivo exige (mesmo já ligada).</param>
/// <param name="Prazo">Último dia para ligar; nulo = sem prazo (operador) ou não exigida.</param>
/// <param name="Pendente">Exigida e ainda desligada.</param>
/// <param name="Bloqueia">Pendente e o prazo acabou (ou não tem prazo): só a tela de ligar abre.</param>
public sealed record ExigenciaDuploFator(bool Exigida, MotivoExigenciaDuploFator Motivo, DateOnly? Prazo, bool Pendente, bool Bloqueia)
{
    public static readonly ExigenciaDuploFator NaoExigida = new(false, MotivoExigenciaDuploFator.Nenhum, null, false, false);
}

/// <summary>Exigência para o usuário logado (SEG1). Implementação na Infrastructure.</summary>
public interface IExigenciaDuploFatorService
{
    Task<ExigenciaDuploFator> AvaliarAsync(Guid usuarioId, CancellationToken cancellationToken);
}

/// <summary>Configurações da conta › Segurança.</summary>
/// <param name="ExigirDeTodos">A conta exige de todos os usuários.</param>
/// <param name="ExigidoDesde">Quando o administrador ligou a exigência.</param>
/// <param name="VoceLigou">O administrador logado já ligou a dele (exigir de todos pede isso).</param>
public sealed record SegurancaContaResponse(
    bool ExigirDeTodos,
    DateTimeOffset? ExigidoDesde,
    bool VoceLigou,
    int Usuarios,
    int UsuariosComDuploFator,
    IReadOnlyList<string> UsuariosSemDuploFator,
    int DiasPrazoAdesao);

public sealed record AlterarSegurancaContaRequest(bool ExigirDeTodos);

public interface ISegurancaContaUseCase
{
    Task<SegurancaContaResponse> ObterAsync(CancellationToken cancellationToken);

    Task<SegurancaContaResponse> AlterarAsync(AlterarSegurancaContaRequest request, CancellationToken cancellationToken);
}
