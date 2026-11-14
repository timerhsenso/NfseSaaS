using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.UseCases.Plataforma;
using NfseSaaS.Application.UseCases.Seguranca;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Web.Filters;
using NfseSaaS.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace NfseSaaS.Web.Controllers.Api;

/// <param name="Lembrar">"Não pedir de novo neste navegador" (cookie do Identity, 14 dias).</param>
public sealed record LoginCodigoRequest(string Codigo, bool Lembrar = false);

public sealed record AtivarDuploFatorRequest(string Codigo);

public sealed record DesligarDuploFatorRequest(string Senha);

/// <summary>Situação da verificação em duas etapas do usuário logado (menu do usuário).</summary>
/// <param name="Obrigatoria">Exigida para este usuário (SEG1): não pode ser desligada.</param>
/// <param name="Motivo">Por que é exigida ("para administradores da conta"…).</param>
/// <param name="Prazo">Último dia para ligar (nulo = sem prazo ou não exigida).</param>
public sealed record SituacaoDuploFatorResponse(
    bool Ligado, string EmailMascarado, bool EmailConfirmado, int DiasLembrarNavegador,
    bool Obrigatoria = false, string? Motivo = null, DateOnly? Prazo = null);

/// <summary>
/// Verificação em duas etapas por e-mail (B3) — ver RegrasDuploFator.
/// A primeira etapa continua em AuthController.Login: com a verificação
/// ligada, a senha certa devolve { requerCodigo } e o código vai por e-mail
/// (direto pelo SMTP, nunca pela fila/log de e-mails).
/// </summary>
[ApiController]
[Route("api/auth")]
[PermitirSemDuploFator]
public sealed class DuploFatorController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IEmailSender _emailSender;
    private readonly IEmailQueueService _emailQueue;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly AppDbContext _db;
    private readonly ILogger<DuploFatorController> _logger;
    private readonly IExigenciaDuploFatorService _exigencia;

    public DuploFatorController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IEmailSender emailSender,
        IEmailQueueService emailQueue,
        IAuditLogWriter auditLogWriter,
        AppDbContext db,
        ILogger<DuploFatorController> logger,
        IExigenciaDuploFatorService exigencia)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _emailSender = emailSender;
        _emailQueue = emailQueue;
        _auditLogWriter = auditLogWriter;
        _db = db;
        _logger = logger;
        _exigencia = exigencia;
    }

    // ---------- Login, etapa 2 ----------

    /// <summary>Confere o código do e-mail e completa o login. Errado conta como tentativa de login (bloqueio após 5).</summary>
    [EnableRateLimiting("AuthSensivel")]
    [HttpPost("login-codigo")]
    public async Task<IActionResult> LoginCodigo([FromBody] LoginCodigoRequest request, CancellationToken cancellationToken)
    {
        var usuario = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (usuario is null)
            return Unauthorized(new { erro = RegrasDuploFator.MensagemSessaoExpirou });

        var resultado = await _signInManager.TwoFactorSignInAsync(
            RegrasDuploFator.Provedor, RegrasDuploFator.NormalizarCodigo(request.Codigo), isPersistent: false, rememberClient: request.Lembrar);

        if (resultado.Succeeded)
        {
            HttpContext.RequestServices.GetRequiredService<IConfirmacaoSenhaRecente>().Marcar(HttpContext, usuario.Id);
            return Ok();
        }

        if (resultado.IsLockedOut)
        {
            await RegistrarAsync("LoginBloqueadoPorTentativas", usuario, cancellationToken);
            return StatusCode(StatusCodes.Status423Locked, new { erro = "Muitas tentativas erradas. Sua conta foi bloqueada temporariamente — tente novamente em alguns minutos." });
        }

        // NotAllowed: a conta foi suspensa/encerrada entre a senha e o código.
        if (resultado.IsNotAllowed)
            return StatusCode(StatusCodes.Status403Forbidden, new { erro = "Não é possível entrar com esta conta agora. Fale com o suporte." });

        return BadRequest(new { erro = RegrasDuploFator.MensagemCodigoInvalido });
    }

    /// <summary>Manda outro código do login (mesmo usuário da etapa 1, ainda dentro dos 5 minutos).</summary>
    [EnableRateLimiting("Recuperacao")]
    [HttpPost("login-codigo/reenviar")]
    public async Task<IActionResult> ReenviarCodigoLogin(CancellationToken cancellationToken)
    {
        var usuario = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (usuario is null)
            return Unauthorized(new { erro = RegrasDuploFator.MensagemSessaoExpirou });

        if (!await EnviarCodigoLoginAsync(_userManager, _emailSender, _logger, usuario, cancellationToken))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { erro = RegrasDuploFator.MensagemEnvioFalhou });

        return Ok(new { email = CodigoExclusaoConta.MascararEmail(usuario.Email!) });
    }

    /// <summary>Gera e manda o código do login. Usado também pelo AuthController.Login.</summary>
    internal static async Task<bool> EnviarCodigoLoginAsync(
        UserManager<ApplicationUser> userManager, IEmailSender emailSender, ILogger logger, ApplicationUser usuario, CancellationToken cancellationToken)
    {
        var codigo = await userManager.GenerateTwoFactorTokenAsync(usuario, RegrasDuploFator.Provedor);
        var envio = await EnviarComPrazoAsync(emailSender, usuario.Email!, RegrasDuploFator.AssuntoLogin(codigo), RegrasDuploFator.Corpo(codigo, ativacao: false), cancellationToken);
        if (!envio.Sucesso)
            logger.LogError("Código de acesso (duas etapas) do usuário {UserId} não foi enviado: {Erro}", usuario.Id, envio.ErroDetalhe);
        return envio.Sucesso;
    }

    /// <summary>
    /// Envia com prazo: a pessoa está esperando na tela de login. SMTP
    /// travado (o padrão do MailKit espera 2 minutos) vira "não conseguimos
    /// enviar, tente de novo" em vez de uma tela parada.
    /// </summary>
    private static async Task<ResultadoEnvioEmail> EnviarComPrazoAsync(
        IEmailSender emailSender, string destinatario, string assunto, string corpo, CancellationToken cancellationToken)
    {
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        prazo.CancelAfter(TimeSpan.FromSeconds(RegrasDuploFator.SegundosParaEnviar));
        try
        {
            return await emailSender.EnviarAsync(destinatario, assunto, corpo, prazo.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ResultadoEnvioEmail(false, $"SMTP não respondeu em {RegrasDuploFator.SegundosParaEnviar} segundos.");
        }
    }

    // ---------- Ligar e desligar (usuário logado) ----------

    [Authorize]
    [HttpGet("duplo-fator")]
    public async Task<IActionResult> Situacao()
    {
        var usuario = await _userManager.GetUserAsync(User);
        if (usuario is null)
            return Unauthorized();

        var exigencia = await _exigencia.AvaliarAsync(usuario.Id, HttpContext.RequestAborted);
        return Ok(new SituacaoDuploFatorResponse(
            usuario.TwoFactorEnabled, CodigoExclusaoConta.MascararEmail(usuario.Email ?? string.Empty), usuario.EmailConfirmed, RegrasDuploFator.DiasLembrarNavegador,
            exigencia.Exigida, exigencia.Exigida ? RegrasDuploFator.TextoMotivo(exigencia.Motivo) : null, exigencia.Prazo));
    }

    /// <summary>Manda o código que liga a verificação (prova que o e-mail recebe).</summary>
    [Authorize]
    [EnableRateLimiting("Recuperacao")]
    [HttpPost("duplo-fator/codigo")]
    public async Task<IActionResult> SolicitarCodigoAtivacao(CancellationToken cancellationToken)
    {
        var usuario = await _userManager.GetUserAsync(User);
        if (usuario is null)
            return Unauthorized();
        if (usuario.TwoFactorEnabled)
            return BadRequest(new { erro = "A verificação em duas etapas já está ligada." });

        var codigo = await _userManager.GenerateUserTokenAsync(usuario, RegrasDuploFator.Provedor, RegrasDuploFator.PropositoAtivacao);
        var envio = await EnviarComPrazoAsync(_emailSender, usuario.Email!, RegrasDuploFator.AssuntoAtivacao(codigo), RegrasDuploFator.Corpo(codigo, ativacao: true), cancellationToken);
        if (!envio.Sucesso)
        {
            _logger.LogError("Código para ligar as duas etapas do usuário {UserId} não foi enviado: {Erro}", usuario.Id, envio.ErroDetalhe);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { erro = RegrasDuploFator.MensagemEnvioFalhou });
        }

        return Ok(new { email = CodigoExclusaoConta.MascararEmail(usuario.Email!) });
    }

    /// <summary>Confere o código e liga. O código provou o e-mail: ele passa a contar como confirmado.</summary>
    [Authorize]
    [EnableRateLimiting("AuthSensivel")]
    [HttpPost("duplo-fator/ativar")]
    public async Task<IActionResult> Ativar([FromBody] AtivarDuploFatorRequest request, CancellationToken cancellationToken)
    {
        var usuario = await _userManager.GetUserAsync(User);
        if (usuario is null)
            return Unauthorized();
        if (usuario.TwoFactorEnabled)
            return Ok();

        var valido = await _userManager.VerifyUserTokenAsync(
            usuario, RegrasDuploFator.Provedor, RegrasDuploFator.PropositoAtivacao, RegrasDuploFator.NormalizarCodigo(request.Codigo));
        if (!valido)
            return BadRequest(new { erro = RegrasDuploFator.MensagemCodigoInvalido });

        if (!usuario.EmailConfirmed)
        {
            usuario.EmailConfirmed = true;
            await _userManager.UpdateAsync(usuario);
        }

        var resultado = await _userManager.SetTwoFactorEnabledAsync(usuario, true);
        if (!resultado.Succeeded)
            return BadRequest(resultado.Errors.Select(e => e.Description));

        // Ligar troca o SecurityStamp: renova o cookie desta sessão para não cair.
        await _signInManager.RefreshSignInAsync(usuario);
        await AvisarAsync(usuario, ligou: true, cancellationToken);
        return Ok();
    }

    /// <summary>Desliga (pede a senha) e esquece os navegadores lembrados.</summary>
    [Authorize]
    [EnableRateLimiting("AuthSensivel")]
    [HttpPost("duplo-fator/desligar")]
    public async Task<IActionResult> Desligar([FromBody] DesligarDuploFatorRequest request, CancellationToken cancellationToken)
    {
        var usuario = await _userManager.GetUserAsync(User);
        if (usuario is null)
            return Unauthorized();
        if (!usuario.TwoFactorEnabled)
            return Ok();

        var exigencia = await _exigencia.AvaliarAsync(usuario.Id, cancellationToken);
        if (exigencia.Exigida)
            return UnprocessableEntity(new { erro = $"A verificação em duas etapas é obrigatória {RegrasDuploFator.TextoMotivo(exigencia.Motivo)}: não dá para desligar." });

        if (!await _userManager.CheckPasswordAsync(usuario, request.Senha ?? string.Empty))
            return BadRequest(new { erro = "Senha incorreta." });

        var resultado = await _userManager.SetTwoFactorEnabledAsync(usuario, false);
        if (!resultado.Succeeded)
            return BadRequest(resultado.Errors.Select(e => e.Description));

        await _signInManager.ForgetTwoFactorClientAsync();
        await _signInManager.RefreshSignInAsync(usuario);
        await AvisarAsync(usuario, ligou: false, cancellationToken);
        return Ok();
    }

    // ---------- Apoio ----------

    private async Task AvisarAsync(ApplicationUser usuario, bool ligou, CancellationToken cancellationToken)
    {
        _auditLogWriter.Registrar(ligou ? "LigarDuploFator" : "DesligarDuploFator", "ApplicationUser", usuario.Id, null);
        await _db.SaveChangesAsync(cancellationToken);

        var assunto = ligou ? "Verificação em duas etapas ligada" : "Verificação em duas etapas desligada";
        await _emailQueue.EnfileirarAsync(TipoEmailCatalogo.AvisoDuploFator, usuario.Id, usuario.Email!, assunto, RegrasDuploFator.CorpoAviso(ligou), cancellationToken);
    }

    /// <summary>Auditoria antes do login (sem claim de conta): grava na conta do usuário.</summary>
    private async Task RegistrarAsync(string operacao, ApplicationUser usuario, CancellationToken cancellationToken)
    {
        _db.TenantIdOverrideDeSistema = usuario.TenantId;
        try
        {
            _auditLogWriter.Registrar(operacao, "ApplicationUser", usuario.Id, null);
            await _db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _db.TenantIdOverrideDeSistema = null;
        }
    }
}
