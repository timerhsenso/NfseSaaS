using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cadastro;
using NfseSaaS.Application.UseCases.Consultas;
using NfseSaaS.Application.Validation;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Primeira Empresa da conta perfil Empresa, criada no próprio cadastro com
/// os dados públicos do CNPJ. Começa em homologação. O e-mail da Empresa é
/// o de quem se cadastrou (o e-mail da Receita costuma ser o do contador).
/// Telefone só entra se tiver o tamanho que a DPS aceita (6 a 20 dígitos).
/// </summary>
public sealed class CriarEmpresaInicialService : ICriarEmpresaInicialService
{
    private readonly AppDbContext _db;

    public CriarEmpresaInicialService(AppDbContext db)
    {
        _db = db;
    }

    public void Adicionar(Guid tenantId, string cnpj, string nome, string emailConta, ConsultaCnpjResponse? consulta)
    {
        var telefone = new string((consulta?.Telefone ?? string.Empty).Where(char.IsDigit).ToArray());

        _db.Empresas.Add(new Empresa
        {
            TenantId = tenantId,
            Cnpj = cnpj,
            RazaoSocial = Limitar(string.IsNullOrWhiteSpace(consulta?.RazaoSocial) ? nome : consulta.RazaoSocial, 200),
            NomeFantasia = string.IsNullOrWhiteSpace(consulta?.NomeFantasia) ? null : Limitar(consulta.NomeFantasia, 200),
            Email = emailConta,
            Telefone = telefone.Length is >= 6 and <= 20 ? telefone : string.Empty,
            Cep = consulta?.Cep ?? string.Empty,
            Logradouro = Limitar(consulta?.Logradouro ?? string.Empty, 200),
            Numero = Limitar(consulta?.Numero ?? string.Empty, 20),
            Complemento = string.IsNullOrWhiteSpace(consulta?.Complemento) ? null : Limitar(consulta.Complemento, 100),
            Bairro = Limitar(consulta?.Bairro ?? string.Empty, 100),
            CodigoMunicipio = consulta?.CodigoMunicipio is { Length: 7 } codigo ? codigo : string.Empty,
            Uf = consulta?.Uf ?? string.Empty,
            // Só sugestão: "dados fiscais" continua pendente até o usuário confirmar.
            OpSimpNac = consulta?.OpcaoSimplesSugerida ?? string.Empty
        });
    }

    private static string Limitar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];
}

/// <summary>
/// Consulta do CNPJ na tela pública de cadastro. Devolve só o que a pessoa
/// precisa para reconhecer a empresa (nome, cidade, situação) e se já existe
/// conta para ele. O limite por IP fica no controller (rate limiting).
/// </summary>
public sealed class ConsultarCnpjCadastroUseCase : IConsultarCnpjCadastroUseCase
{
    private readonly IConsultarCnpjUseCase _consultarCnpj;
    private readonly AppDbContext _db;

    public ConsultarCnpjCadastroUseCase(IConsultarCnpjUseCase consultarCnpj, AppDbContext db)
    {
        _consultarCnpj = consultarCnpj;
        _db = db;
    }

    public async Task<ConsultaCnpjCadastroResponse> ExecutarAsync(string cnpj, CancellationToken cancellationToken)
    {
        if (!DocumentoFiscalValidator.CnpjValido(cnpj))
            throw new RegraNegocioException("CNPJ inválido (confira os números).");

        var documento = RegrasCadastroConta.NormalizarDocumento(cnpj);
        var jaTemConta = await _db.Tenants.AsNoTracking().AnyAsync(t => t.Documento == documento, cancellationToken);
        var consulta = await _consultarCnpj.ExecutarAsync(documento, cancellationToken);

        return new ConsultaCnpjCadastroResponse(
            documento, consulta.RazaoSocial, consulta.NomeFantasia, consulta.Municipio, consulta.Uf,
            consulta.Ativa, consulta.SituacaoCadastral, jaTemConta);
    }
}

/// <summary>
/// Confirmação do e-mail pelo token do próprio ASP.NET Core Identity
/// (GenerateEmailConfirmationTokenAsync, validade padrão de 1 dia). O link
/// vai pela fila de e-mails, como o de redefinição de senha. Convite aceito
/// e senha redefinida pelo link também confirmam o e-mail (AuthController):
/// os dois provam acesso à caixa de entrada.
/// </summary>
public sealed class ConfirmacaoEmailService : IConfirmacaoEmailService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;
    private readonly IEmailQueueService _emailQueue;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly EmailOptions _emailOptions;
    private readonly ILogger<ConfirmacaoEmailService> _logger;

    public ConfirmacaoEmailService(
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        IEmailQueueService emailQueue,
        IAuditLogWriter auditLogWriter,
        IOptions<EmailOptions> emailOptions,
        ILogger<ConfirmacaoEmailService> logger)
    {
        _userManager = userManager;
        _db = db;
        _emailQueue = emailQueue;
        _auditLogWriter = auditLogWriter;
        _emailOptions = emailOptions.Value;
        _logger = logger;
    }

    public async Task EnviarAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        var usuario = await _userManager.FindByIdAsync(usuarioId.ToString());
        if (usuario is null || usuario.EmailConfirmed || string.IsNullOrWhiteSpace(usuario.Email))
            return;

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(usuario);
        var link = string.IsNullOrWhiteSpace(_emailOptions.AppBaseUrl)
            ? null
            : $"{_emailOptions.AppBaseUrl.TrimEnd('/')}/Account/ConfirmarEmail?u={usuario.Id}&t={Uri.EscapeDataString(token)}";
        if (link is null)
            _logger.LogWarning("Email:AppBaseUrl vazio: o e-mail de confirmação de {UsuarioId} sai sem link.", usuario.Id);

        var corpo = $"""
            <p>Olá! Falta só confirmar o seu e-mail no <strong>micoempresa</strong>.</p>
            {(link is not null ? $"""<p><a href="{WebUtility.HtmlEncode(link)}" style="display:inline-block;padding:10px 18px;background:#16a34a;color:#fff;border-radius:6px;text-decoration:none">Confirmar meu e-mail</a></p>""" : "")}
            <p>É por este e-mail que chegam as notas, os avisos do certificado e da cobrança. Sem confirmar, você usa tudo e emite em teste (homologação), mas ainda não emite notas de verdade (produção).</p>
            <p style="color:#64748b;font-size:12px">O link vale por 24 horas. Se você não criou uma conta no micoempresa, ignore este e-mail.</p>
            """;

        // Pode ser chamado antes de existir login (no próprio cadastro).
        _db.TenantIdOverrideDeSistema = usuario.TenantId;
        try
        {
            await _emailQueue.EnfileirarAsync(
                new EmailParaEnfileirar(TipoEmailCatalogo.ConfirmacaoEmail, usuario.Id, usuario.Email, "Confirme seu e-mail — micoempresa", corpo, Segredos: new[] { token }),
                cancellationToken);
        }
        finally
        {
            _db.TenantIdOverrideDeSistema = null;
        }
    }

    public async Task<bool> ConfirmarAsync(Guid usuarioId, string token, CancellationToken cancellationToken)
    {
        var usuario = await _userManager.FindByIdAsync(usuarioId.ToString());
        if (usuario is null || string.IsNullOrWhiteSpace(token))
            return false;
        if (usuario.EmailConfirmed)
            return true;

        var resultado = await _userManager.ConfirmEmailAsync(usuario, token);
        if (!resultado.Succeeded)
            return false;

        _db.TenantIdOverrideDeSistema = usuario.TenantId;
        try
        {
            _auditLogWriter.Registrar("EmailConfirmado", "ApplicationUser", usuario.Id, null);
            await _db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _db.TenantIdOverrideDeSistema = null;
        }
        return true;
    }

    public Task<bool> ContaConfirmadaAsync(Guid tenantId, CancellationToken cancellationToken) =>
        _db.Users.AsNoTracking().AnyAsync(u => u.TenantId == tenantId && u.EmailConfirmed, cancellationToken);
}
