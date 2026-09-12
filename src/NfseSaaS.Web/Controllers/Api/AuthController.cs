using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers.Api;

public sealed record RegistrarRequest(string Email, string Senha, string RazaoSocialTenant, string CnpjTenant);

public sealed record LoginRequest(string Email, string Senha);

public sealed record AlterarSenhaRequest(string SenhaAtual, string NovaSenha);

public sealed record SolicitarRedefinicaoSenhaRequest(string Email);

public sealed record RedefinirSenhaRequest(string Email, string Token, string NovaSenha);

public sealed record ConvidarRequest(string Email, Guid GrupoId);

public sealed record AceitarConviteRequest(string Email, string Token, string NovaSenha);

public sealed record AlterarGrupoRequest(Guid GrupoId);

/// <summary>Linha da grid de usuários — ver AuthController.ListarUsuarios.</summary>
public sealed record UsuarioResponse(
    Guid Id,
    string Email,
    Guid? GrupoId,
    string Grupo,
    DateTimeOffset? ConvidadoEm,
    DateTimeOffset? ConviteAceitoEm,
    bool Bloqueado,
    bool EhVoce)
{
    /// <summary>Convidado mas ainda não aceitou. Falso pro primeiro Administrador de cada Tenant (não passou por convite).</summary>
    public bool Pendente => ConvidadoEm is not null && ConviteAceitoEm is null;
}

/// <summary>
/// Autenticação, bootstrap de Tenant e gestão de usuário (convite,
/// grupo de permissão, bloqueio, exclusão) do mesmo Tenant. O primeiro
/// registro de cada Tenant cria a conta administradora; usuários
/// adicionais entram via convite (ver Convidar/AceitarConvite).
///
/// Convite reaproveita o mecanismo de token de redefinição de senha do
/// próprio ASP.NET Core Identity (o mesmo de "esqueci minha senha") — não
/// há geração de token própria.
///
/// GrupoId (módulo de segurança IAEC — Grupo/GrupoTela) é a ÚNICA fonte
/// de autorização de tela desde que os 9 controllers de negócio
/// (Empresas, Clientes, Servicos, Contratos, Nfse, NotaMensal,
/// AuditLogs) migraram de [Authorize(Roles=...)] pra [RequerPermissao].
/// A Role antiga do Identity (AspNetRoles/AspNetUserRoles) não é mais
/// escrita nem lida por nenhum controller — continua existindo só como
/// dado histórico (RoleManager/IdentitySeeder seguem seedando os 5
/// papéis por segurança, mas nada os atribui a usuário novo).
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IGrupoProvisionamentoService _grupoProvisionamento;
    private readonly IEmailQueueService _emailQueueService;
    private readonly EmailOptions _emailOptions;
    private readonly IHostEnvironment _environment;

    public AuthController(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        IAuditLogWriter auditLogWriter,
        IGrupoProvisionamentoService grupoProvisionamento,
        IEmailQueueService emailQueueService,
        IOptions<EmailOptions> emailOptions,
        IHostEnvironment environment)
    {
        _db = db;
        _userManager = userManager;
        _signInManager = signInManager;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _auditLogWriter = auditLogWriter;
        _grupoProvisionamento = grupoProvisionamento;
        _emailQueueService = emailQueueService;
        _emailOptions = emailOptions.Value;
        _environment = environment;
    }

    /// <summary>Cria um novo Tenant, provisiona os 5 grupos padrão e cria o primeiro usuário (grupo Administrador), já autenticado (cookie com o Claim tenant_id).</summary>
    [HttpPost("registrar")]
    public async Task<IActionResult> Registrar([FromBody] RegistrarRequest request, CancellationToken cancellationToken)
    {
        var tenant = new Tenant
        {
            Cnpj = request.CnpjTenant,
            RazaoSocial = request.RazaoSocialTenant
        };

        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(cancellationToken);

        var gruposPorNome = await _grupoProvisionamento.ProvisionarGruposPadraoAsync(tenant.Id, cancellationToken);

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            TenantId = tenant.Id,
            GrupoId = gruposPorNome[Papeis.Administrador]
        };

        var resultado = await _userManager.CreateAsync(user, request.Senha);

        if (!resultado.Succeeded)
            return BadRequest(resultado.Errors.Select(e => e.Description));

        await _signInManager.SignInAsync(user, isPersistent: false);

        return Ok(new { tenantId = tenant.Id, userId = user.Id });
    }

    /// <summary>
    /// lockoutOnFailure:true — 5 tentativas erradas bloqueiam a conta por
    /// 15 minutos (ver options.Lockout em DependencyInjection.cs). O
    /// bloqueio em si é gravado em auditoria; tentativa errada isolada
    /// (sem chegar a bloquear) não gera log — seria ruído demais.
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var usuario = await _userManager.FindByEmailAsync(request.Email);

        var resultado = await _signInManager.PasswordSignInAsync(
            request.Email, request.Senha, isPersistent: false, lockoutOnFailure: true);

        if (resultado.IsLockedOut && usuario is not null)
        {
            // Login bloqueado acontece ANTES de qualquer autenticação —
            // não há Claim de tenant pra ApplyTenantIsolation resolver
            // sozinho (AuditLog é ITenantEntity). Ver
            // TenantIdOverrideDeSistema em AppDbContext.
            _db.TenantIdOverrideDeSistema = usuario.TenantId;
            try
            {
                _auditLogWriter.Registrar("LoginBloqueadoPorTentativas", "ApplicationUser", usuario.Id, null);
                await _db.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                _db.TenantIdOverrideDeSistema = null;
            }

            return StatusCode(StatusCodes.Status423Locked, new { erro = "Muitas tentativas de login erradas. Sua conta foi bloqueada temporariamente — tente novamente em alguns minutos." });
        }

        return resultado.Succeeded ? Ok() : Unauthorized();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return Ok();
    }

    /// <summary>
    /// Troca a própria senha — disponível pra qualquer usuário
    /// autenticado, sem depender de Grupo/permissão de nenhuma Tela (não
    /// é uma ação sobre outro usuário, é sobre a própria conta). Exige a
    /// senha atual (UserManager.ChangePasswordAsync já valida isso) —
    /// nunca troca sem confirmar quem está pedindo sabe a senha de hoje.
    /// </summary>
    [Authorize]
    [HttpPost("alterar-senha")]
    public async Task<IActionResult> AlterarSenha([FromBody] AlterarSenhaRequest request, CancellationToken cancellationToken)
    {
        var usuario = await _userManager.GetUserAsync(User);
        if (usuario is null)
            return Unauthorized();

        var resultado = await _userManager.ChangePasswordAsync(usuario, request.SenhaAtual, request.NovaSenha);
        if (!resultado.Succeeded)
            return BadRequest(resultado.Errors.Select(e => e.Description));

        // Trocar a senha muda o SecurityStamp do Identity — o cookie
        // atual ficaria invalidado no meio da resposta se não
        // re-autenticar aqui, derrubando o usuário sem aviso.
        await _signInManager.RefreshSignInAsync(usuario);

        _auditLogWriter.Registrar("AlterarPropriaSenha", "ApplicationUser", usuario.Id, null);
        await _db.SaveChangesAsync(cancellationToken);

        await EnfileirarEmailAvisoTrocaSenhaAsync(usuario.Id, usuario.Email!, cancellationToken);

        return Ok();
    }

    /// <summary>
    /// Primeiro passo do "esqueci minha senha": gera o token (mesmo
    /// mecanismo do convite) e envia por e-mail. A resposta é SEMPRE a
    /// mesma, exista ou não o e-mail na base — diferenciar a resposta
    /// viraria uma forma de descobrir quem é cliente do sistema
    /// (enumeração de usuário), então nunca revela isso.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("esqueci-senha")]
    public async Task<IActionResult> SolicitarRedefinicaoSenha([FromBody] SolicitarRedefinicaoSenhaRequest request, CancellationToken cancellationToken)
    {
        var usuario = await _userManager.FindByEmailAsync(request.Email);

        if (usuario is not null)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(usuario);
            var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == usuario.TenantId, cancellationToken);

            // Pré-autenticação (AllowAnonymous) — não há Claim de tenant
            // pra ApplyTenantIsolation resolver sozinho (EmailLog e
            // AuditLog são ITenantEntity). Ver TenantIdOverrideDeSistema
            // em AppDbContext. Cobre tanto o enfileiramento do e-mail
            // quanto o log de auditoria logo abaixo.
            _db.TenantIdOverrideDeSistema = usuario.TenantId;
            try
            {
                await EnfileirarEmailDeRedefinicaoSenhaAsync(usuario.Id, usuario.Email!, token, tenant?.RazaoSocial, cancellationToken);

                _auditLogWriter.Registrar("SolicitarRedefinicaoSenha", "ApplicationUser", usuario.Id, null);
                await _db.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                _db.TenantIdOverrideDeSistema = null;
            }
        }

        return Ok(new { mensagem = "Se esse e-mail existir na nossa base, enviamos um link para redefinir a senha." });
    }

    /// <summary>
    /// Segundo passo do "esqueci minha senha": define a nova senha a
    /// partir do token recebido por e-mail e já autentica (mesmo padrão
    /// de AceitarConvite). Também limpa um bloqueio de login ativo — ter
    /// clicado no link do e-mail já prova controle da caixa de entrada
    /// cadastrada, então é uma prova de identidade aceitável pra
    /// desbloquear.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("redefinir-senha")]
    public async Task<IActionResult> RedefinirSenha([FromBody] RedefinirSenhaRequest request, CancellationToken cancellationToken)
    {
        var usuario = await _userManager.FindByEmailAsync(request.Email);
        if (usuario is null)
            return BadRequest(new { erro = "Link inválido ou expirado." });

        var resultado = await _userManager.ResetPasswordAsync(usuario, request.Token, request.NovaSenha);
        if (!resultado.Succeeded)
            return BadRequest(resultado.Errors.Select(e => e.Description));

        await _userManager.SetLockoutEndDateAsync(usuario, null);

        await _signInManager.SignInAsync(usuario, isPersistent: false);

        _auditLogWriter.Registrar("RedefinirSenha", "ApplicationUser", usuario.Id, null);
        await _db.SaveChangesAsync(cancellationToken);

        await EnfileirarEmailAvisoTrocaSenhaAsync(usuario.Id, usuario.Email!, cancellationToken);

        return Ok();
    }

    /// <summary>
    /// Convida um e-mail para entrar no MESMO Tenant do usuário autenticado,
    /// já atribuído a um Grupo de permissão. Cria a conta (sem senha
    /// utilizável), gera um token de convite e enfileira o e-mail — o
    /// envio de verdade roda em segundo plano (ver
    /// EmailDispatchHostedService), a requisição não espera o SMTP. Status
    /// do envio (Pendente/Enviado/Falhou) e reenvio ficam na tela de
    /// E-mails.
    /// </summary>
    [RequerPermissao(TelaCatalogo.Usuarios, AcaoPermissao.Incluir)]
    [HttpPost("convidar")]
    public async Task<IActionResult> Convidar([FromBody] ConvidarRequest request, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            return Unauthorized();

        var grupo = await _db.Grupos.FirstOrDefaultAsync(g => g.Id == request.GrupoId && g.TenantId == tenantId, cancellationToken);
        if (grupo is null)
            return BadRequest(new { erro = "Grupo inválido." });

        var usuarioExistente = await _userManager.FindByEmailAsync(request.Email);
        if (usuarioExistente is not null)
            return Conflict(new { erro = "Já existe um usuário com este e-mail." });

        var novoUsuario = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            TenantId = tenantId,
            EmailConfirmed = false,
            ConvidadoEm = DateTimeOffset.UtcNow,
            GrupoId = grupo.Id
        };

        // Senha aleatória inicial, nunca usada — o convite só é utilizável
        // através do token de AceitarConvite (ResetPasswordAsync).
        var senhaAleatoriaInicial = Guid.NewGuid().ToString("N") + "Aa1!";
        var resultado = await _userManager.CreateAsync(novoUsuario, senhaAleatoriaInicial);

        if (!resultado.Succeeded)
            return BadRequest(resultado.Errors.Select(e => e.Description));

        var token = await _userManager.GeneratePasswordResetTokenAsync(novoUsuario);

        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        var emailLogId = await EnfileirarEmailDeConviteAsync(novoUsuario.Id, request.Email, token, tenant?.RazaoSocial, cancellationToken);

        var resposta = new Dictionary<string, object?>
        {
            ["userId"] = novoUsuario.Id,
            ["email"] = novoUsuario.Email,
            ["emailLogId"] = emailLogId
        };

        // O envio de verdade roda em segundo plano (ver EmailDispatchHostedService)
        // — a requisição não sabe mais na hora se saiu ou não. Acompanhe
        // o status na tela de E-mails; o token só volta aqui em
        // Development, pra testar via Swagger sem precisar de SMTP.
        if (_environment.IsDevelopment())
            resposta["token"] = token;

        return Ok(resposta);
    }

    /// <summary>Aceita um convite: define a senha da conta criada em Convidar (via token) e já autentica.</summary>
    [AllowAnonymous]
    [HttpPost("aceitar-convite")]
    public async Task<IActionResult> AceitarConvite([FromBody] AceitarConviteRequest request)
    {
        var usuario = await _userManager.FindByEmailAsync(request.Email);
        if (usuario is null)
            return NotFound();

        var resultado = await _userManager.ResetPasswordAsync(usuario, request.Token, request.NovaSenha);
        if (!resultado.Succeeded)
            return BadRequest(resultado.Errors.Select(e => e.Description));

        // Marca o convite como aceito — é este campo (não EmailConfirmed,
        // que o Identity usa pra outra finalidade) que a listagem de
        // usuários usa pra decidir o Status e bloquear reenvio/exclusão.
        usuario.ConviteAceitoEm = DateTimeOffset.UtcNow;
        await _userManager.UpdateAsync(usuario);

        await _signInManager.SignInAsync(usuario, isPersistent: false);

        return Ok();
    }

    /// <summary>
    /// Lista TODOS os usuários do Tenant atual (não só convites pendentes —
    /// inclui quem já aceitou e o próprio Administrador criado em
    /// Registrar), com grupo e status calculado (Pendente/Bloqueado/Ativo
    /// — ver UsuarioResponse.Pendente e o campo Bloqueado). É a única
    /// tela de "quem tem acesso ao sistema". AspNetUsers não implementa
    /// ITenantEntity (não sofre o Global Query Filter), então o filtro
    /// por tenant aqui é manual, de propósito.
    ///
    /// O nome do grupo vem de um LEFT JOIN simples (não de uma subquery
    /// com Take(1), como era com Role) porque GrupoId agora é uma FK
    /// única no próprio ApplicationUser — não tem mais como um usuário
    /// ter "mais de um grupo" por construção, o problema que forçou a
    /// subquery antes não existe mais nesse desenho.
    /// </summary>
    [RequerPermissao(TelaCatalogo.Usuarios, AcaoPermissao.Consultar)]
    [HttpGet("usuarios")]
    public async Task<IActionResult> ListarUsuarios(CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            return Unauthorized();

        var agora = DateTimeOffset.UtcNow;

        var usuarios = await (
            from u in _db.Users.AsNoTracking()
            where u.TenantId == tenantId
            join g in _db.Grupos.AsNoTracking() on u.GrupoId equals g.Id into grupos
            from g in grupos.DefaultIfEmpty()
            orderby u.Email
            select new UsuarioResponse(
                u.Id,
                u.Email!,
                u.GrupoId,
                g != null ? g.Nome : "-",
                u.ConvidadoEm,
                u.ConviteAceitoEm,
                u.LockoutEnd != null && u.LockoutEnd > agora,
                u.Id == _currentUser.UserId)
        ).ToListAsync(cancellationToken);

        return Ok(usuarios);
    }

    /// <summary>
    /// Troca o Grupo de permissão de um usuário existente — atribuição
    /// DIRETA (GrupoId é uma FK única, sem a dança de Remove+Add que a
    /// Role do Identity exigia). Bloqueado se o usuário for o único
    /// Administrador do Tenant (ver EhUnicoAdministradorAsync) — senão o
    /// próprio Tenant ficaria sem ninguém pra gerenciar usuários.
    /// </summary>
    [RequerPermissao(TelaCatalogo.Usuarios, AcaoPermissao.Alterar)]
    [HttpPut("usuarios/{id:guid}/grupo")]
    public async Task<IActionResult> AlterarGrupo(Guid id, [FromBody] AlterarGrupoRequest request, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            return Unauthorized();

        var novoGrupo = await _db.Grupos.FirstOrDefaultAsync(g => g.Id == request.GrupoId && g.TenantId == tenantId, cancellationToken);
        if (novoGrupo is null)
            return BadRequest(new { erro = "Grupo inválido." });

        var usuario = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, cancellationToken);
        if (usuario is null)
            return NotFound();

        if (usuario.GrupoId == novoGrupo.Id)
            return NoContent();

        if (await EhUnicoAdministradorAsync(tenantId, usuario.Id, cancellationToken))
            return Conflict(new { erro = "Este é o único Administrador do Tenant — promova outro usuário antes de trocar o grupo dele." });

        var grupoAnterior = usuario.GrupoId;
        usuario.GrupoId = novoGrupo.Id;

        _auditLogWriter.Registrar("AlterarGrupoUsuario", "ApplicationUser", usuario.Id, new { usuario.Email, grupoAnterior, grupoNovo = novoGrupo.Id });
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Bloqueia o login do usuário (Lockout do próprio Identity, sem
    /// excluir a conta — LockoutEnabled já vem true por padrão em todo
    /// usuário criado via CreateAsync). Não permite bloquear a própria
    /// conta nem o único Administrador do Tenant.
    /// </summary>
    [RequerPermissao(TelaCatalogo.Usuarios, AcaoPermissao.Alterar)]
    [HttpPost("usuarios/{id:guid}/bloquear")]
    public async Task<IActionResult> Bloquear(Guid id, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            return Unauthorized();

        if (_currentUser.UserId == id)
            return Conflict(new { erro = "Não é possível bloquear a própria conta." });

        var usuario = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, cancellationToken);
        if (usuario is null)
            return NotFound();

        if (await EhUnicoAdministradorAsync(tenantId, usuario.Id, cancellationToken))
            return Conflict(new { erro = "Este é o único Administrador do Tenant — não é possível bloqueá-lo." });

        await _userManager.SetLockoutEndDateAsync(usuario, DateTimeOffset.MaxValue);

        _auditLogWriter.Registrar("BloquearUsuario", "ApplicationUser", usuario.Id, new { usuario.Email });
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Remove o bloqueio de login (ver Bloquear).</summary>
    [RequerPermissao(TelaCatalogo.Usuarios, AcaoPermissao.Alterar)]
    [HttpPost("usuarios/{id:guid}/desbloquear")]
    public async Task<IActionResult> Desbloquear(Guid id, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            return Unauthorized();

        var usuario = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, cancellationToken);
        if (usuario is null)
            return NotFound();

        await _userManager.SetLockoutEndDateAsync(usuario, null);

        _auditLogWriter.Registrar("DesbloquearUsuario", "ApplicationUser", usuario.Id, new { usuario.Email });
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Reseta a senha de um usuário a pedido do Administrador — NUNCA
    /// manda senha nenhuma por e-mail (texto puro em trânsito é um
    /// vazamento em potencial, e uma "senha padrão" é adivinhável antes
    /// do usuário trocar). Em vez disso: troca pra uma senha aleatória
    /// que não fica guardada em lugar nenhum (nem log), o que já
    /// invalida a senha antiga e derruba qualquer sessão ativa dele
    /// (troca de senha sempre atualiza o SecurityStamp do Identity) — e
    /// manda o mesmo link de "definir nova senha" do fluxo de
    /// redefinição normal, avisando que foi o Administrador quem
    /// resetou. Diferença real pro "esqueci minha senha" comum: lá a
    /// senha antiga continua válida até o usuário completar a troca;
    /// aqui ela já morre na hora do clique do Administrador.
    /// </summary>
    [RequerPermissao(TelaCatalogo.Usuarios, AcaoPermissao.Alterar)]
    [HttpPost("usuarios/{id:guid}/resetar-senha")]
    public async Task<IActionResult> ResetarSenha(Guid id, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            return Unauthorized();

        var usuario = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, cancellationToken);
        if (usuario is null)
            return NotFound();

        var tokenParaZerar = await _userManager.GeneratePasswordResetTokenAsync(usuario);
        var senhaAleatoriaDescartavel = Guid.NewGuid().ToString("N") + "Aa1!"; // gerada, usada uma vez pra invalidar a antiga, e esquecida — nunca persiste em lugar nenhum
        var resultadoZerar = await _userManager.ResetPasswordAsync(usuario, tokenParaZerar, senhaAleatoriaDescartavel);
        if (!resultadoZerar.Succeeded)
            return BadRequest(resultadoZerar.Errors.Select(e => e.Description));

        // O ResetPasswordAsync acima já mudou o SecurityStamp — o token
        // usado pra zerar não serve mais pro link que o usuário vai
        // clicar, precisa gerar um novo em cima do stamp atualizado.
        var tokenParaEmail = await _userManager.GeneratePasswordResetTokenAsync(usuario);

        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        var emailLogId = await EnfileirarEmailDeResetPorAdminAsync(usuario.Id, usuario.Email!, tokenParaEmail, tenant?.RazaoSocial, cancellationToken);

        _auditLogWriter.Registrar("ResetarSenhaUsuario", "ApplicationUser", usuario.Id, new { usuario.Email });
        await _db.SaveChangesAsync(cancellationToken);

        var resposta = new Dictionary<string, object?>
        {
            ["userId"] = usuario.Id,
            ["email"] = usuario.Email,
            ["emailLogId"] = emailLogId
        };

        if (_environment.IsDevelopment())
            resposta["token"] = tokenParaEmail;

        return Ok(resposta);
    }

    /// <summary>
    /// Reenvia o e-mail de convite (novo token — o anterior continua
    /// válido também, o Identity não invalida tokens antigos ao gerar um
    /// novo). Só funciona pra convite ainda pendente.
    /// </summary>
    [RequerPermissao(TelaCatalogo.Usuarios, AcaoPermissao.Alterar)]
    [HttpPost("usuarios/{id:guid}/reenviar-convite")]
    public async Task<IActionResult> ReenviarConvite(Guid id, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            return Unauthorized();

        var usuario = await _db.Users.FirstOrDefaultAsync(
            u => u.Id == id && u.TenantId == tenantId && u.ConvidadoEm != null, cancellationToken);
        if (usuario is null)
            return NotFound();

        if (usuario.ConviteAceitoEm is not null)
            return Conflict(new { erro = "Este convite já foi aceito — não é possível reenviar." });

        var token = await _userManager.GeneratePasswordResetTokenAsync(usuario);

        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        var emailLogId = await EnfileirarEmailDeConviteAsync(usuario.Id, usuario.Email!, token, tenant?.RazaoSocial, cancellationToken);

        var resposta = new Dictionary<string, object?>
        {
            ["userId"] = usuario.Id,
            ["email"] = usuario.Email,
            ["emailLogId"] = emailLogId
        };

        if (_environment.IsDevelopment())
            resposta["token"] = token;

        return Ok(resposta);
    }

    /// <summary>
    /// Exclusão REAL de um usuário — funciona tanto pra convite ainda
    /// pendente quanto pra usuário já ativo (nenhuma tabela de negócio
    /// tem FK pra AspNetUsers — AuditLog.UserId é um Guid solto, sem
    /// relação configurada — então não há histórico bloqueando isto).
    /// Não permite excluir a própria conta nem o único Administrador do
    /// Tenant.
    /// </summary>
    [RequerPermissao(TelaCatalogo.Usuarios, AcaoPermissao.Excluir)]
    [HttpDelete("usuarios/{id:guid}")]
    public async Task<IActionResult> ExcluirUsuario(Guid id, CancellationToken cancellationToken)
    {
        if (_currentTenant.TenantId is not { } tenantId)
            return Unauthorized();

        if (_currentUser.UserId == id)
            return Conflict(new { erro = "Não é possível excluir a própria conta." });

        var usuario = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, cancellationToken);
        if (usuario is null)
            return NotFound();

        if (await EhUnicoAdministradorAsync(tenantId, usuario.Id, cancellationToken))
            return Conflict(new { erro = "Este é o único Administrador do Tenant — não é possível excluí-lo." });

        var email = usuario.Email;
        var resultado = await _userManager.DeleteAsync(usuario);
        if (!resultado.Succeeded)
            return BadRequest(resultado.Errors.Select(e => e.Description));

        _auditLogWriter.Registrar("ExcluirUsuario", "ApplicationUser", id, new { email });
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }


    /// <summary>
    /// True se o usuário informado pertencer ao grupo Administrador
    /// padrão (Grupo.EhAdministrador) e não houver nenhum outro usuário
    /// no mesmo grupo dentro do Tenant — usado pra bloquear ações
    /// (trocar grupo, bloquear, excluir) que deixariam o Tenant sem
    /// ninguém capaz de gerenciar usuários/permissões.
    /// </summary>
    private async Task<bool> EhUnicoAdministradorAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken)
    {
        var usuario = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == usuarioId, cancellationToken);
        if (usuario?.GrupoId is not { } grupoId)
            return false;

        var grupo = await _db.Grupos.AsNoTracking().FirstOrDefaultAsync(g => g.Id == grupoId, cancellationToken);
        if (grupo is not { EhAdministrador: true })
            return false;

        var totalNoGrupo = await _db.Users.AsNoTracking()
            .CountAsync(u => u.TenantId == tenantId && u.GrupoId == grupoId, cancellationToken);

        return totalNoGrupo <= 1;
    }

    /// <summary>Monta e enfileira o e-mail de convite (ver IEmailQueueService) — nunca envia direto, nunca bloqueia a requisição esperando SMTP.</summary>
    private Task<Guid> EnfileirarEmailDeConviteAsync(Guid usuarioId, string destinatarioEmail, string token, string? razaoSocialTenant, CancellationToken cancellationToken)
    {
        var nomeExibicao = string.IsNullOrWhiteSpace(razaoSocialTenant) ? "NfseSaaS" : razaoSocialTenant;

        var linkAceite = string.IsNullOrWhiteSpace(_emailOptions.AppBaseUrl)
            ? null
            : $"{_emailOptions.AppBaseUrl.TrimEnd('/')}/Account/AceitarConvite?email={Uri.EscapeDataString(destinatarioEmail)}&token={Uri.EscapeDataString(token)}";

        var corpoHtml = $"""
            <p>Você foi convidado para acessar o <strong>{WebUtility.HtmlEncode(nomeExibicao)}</strong> no NfseSaaS.</p>
            {(linkAceite is not null ? $"""<p><a href="{linkAceite}">Clique aqui para definir sua senha e aceitar o convite</a></p>""" : "")}
            <p>Se o link acima não abrir uma tela (ou se preferir usar via API), utilize estes dados no aceite do convite:</p>
            <ul>
                <li>E-mail: {WebUtility.HtmlEncode(destinatarioEmail)}</li>
                <li>Token: {WebUtility.HtmlEncode(token)}</li>
            </ul>
            """;

        return _emailQueueService.EnfileirarAsync(TipoEmailCatalogo.Convite, usuarioId, destinatarioEmail, $"Convite — {nomeExibicao}", corpoHtml, cancellationToken);
    }

    private Task<Guid> EnfileirarEmailDeRedefinicaoSenhaAsync(Guid usuarioId, string destinatarioEmail, string token, string? razaoSocialTenant, CancellationToken cancellationToken)
    {
        var nomeExibicao = string.IsNullOrWhiteSpace(razaoSocialTenant) ? "NfseSaaS" : razaoSocialTenant;

        var linkRedefinicao = string.IsNullOrWhiteSpace(_emailOptions.AppBaseUrl)
            ? null
            : $"{_emailOptions.AppBaseUrl.TrimEnd('/')}/Account/RedefinirSenha?email={Uri.EscapeDataString(destinatarioEmail)}&token={Uri.EscapeDataString(token)}";

        var corpoHtml = $"""
            <p>Recebemos um pedido para redefinir sua senha no <strong>{WebUtility.HtmlEncode(nomeExibicao)}</strong> (NfseSaaS).</p>
            {(linkRedefinicao is not null ? $"""<p><a href="{linkRedefinicao}">Clique aqui para definir uma nova senha</a></p>""" : "")}
            <p>Se você não pediu essa redefinição, pode ignorar este e-mail — sua senha atual continua valendo.</p>
            <p>Se o link acima não abrir uma tela (ou se preferir usar via API), utilize estes dados na redefinição:</p>
            <ul>
                <li>E-mail: {WebUtility.HtmlEncode(destinatarioEmail)}</li>
                <li>Token: {WebUtility.HtmlEncode(token)}</li>
            </ul>
            """;

        return _emailQueueService.EnfileirarAsync(TipoEmailCatalogo.EsqueciSenha, usuarioId, destinatarioEmail, $"Redefinição de senha — {nomeExibicao}", corpoHtml, cancellationToken);
    }

    /// <summary>Variação de EnfileirarEmailDeRedefinicaoSenhaAsync — mesmo link, mas avisando que foi o Administrador quem resetou (não um pedido do próprio usuário).</summary>
    private Task<Guid> EnfileirarEmailDeResetPorAdminAsync(Guid usuarioId, string destinatarioEmail, string token, string? razaoSocialTenant, CancellationToken cancellationToken)
    {
        var nomeExibicao = string.IsNullOrWhiteSpace(razaoSocialTenant) ? "NfseSaaS" : razaoSocialTenant;

        var linkRedefinicao = string.IsNullOrWhiteSpace(_emailOptions.AppBaseUrl)
            ? null
            : $"{_emailOptions.AppBaseUrl.TrimEnd('/')}/Account/RedefinirSenha?email={Uri.EscapeDataString(destinatarioEmail)}&token={Uri.EscapeDataString(token)}";

        var corpoHtml = $"""
            <p>Sua senha no <strong>{WebUtility.HtmlEncode(nomeExibicao)}</strong> (NfseSaaS) foi resetada por um Administrador.</p>
            <p>Sua senha anterior não vale mais — defina uma nova pra voltar a acessar o sistema.</p>
            {(linkRedefinicao is not null ? $"""<p><a href="{linkRedefinicao}">Clique aqui para definir sua nova senha</a></p>""" : "")}
            <p>Se o link acima não abrir uma tela (ou se preferir usar via API), utilize estes dados na redefinição:</p>
            <ul>
                <li>E-mail: {WebUtility.HtmlEncode(destinatarioEmail)}</li>
                <li>Token: {WebUtility.HtmlEncode(token)}</li>
            </ul>
            """;

        return _emailQueueService.EnfileirarAsync(TipoEmailCatalogo.ResetSenha, usuarioId, destinatarioEmail, $"Sua senha foi resetada — {nomeExibicao}", corpoHtml, cancellationToken);
    }

    /// <summary>Disparado em TODA troca de senha (alterar ou redefinir por "esqueci") — detecção de troca que o dono da conta não reconhece.</summary>
    private Task<Guid> EnfileirarEmailAvisoTrocaSenhaAsync(Guid usuarioId, string destinatarioEmail, CancellationToken cancellationToken)
    {
        var corpoHtml = """
            <p>Sua senha no NfseSaaS foi alterada agora.</p>
            <p>Se foi você, pode ignorar este e-mail.</p>
            <p><strong>Se você não reconhece esta troca</strong>, entre em contato com o Administrador do seu Tenant o quanto antes.</p>
            """;

        return _emailQueueService.EnfileirarAsync(TipoEmailCatalogo.AvisoTrocaSenha, usuarioId, destinatarioEmail, "Sua senha foi alterada — NfseSaaS", corpoHtml, cancellationToken);
    }
}
