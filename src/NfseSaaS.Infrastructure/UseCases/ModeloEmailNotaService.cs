using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Aparencia;
using NfseSaaS.Application.UseCases.EmailNota;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Configurações da conta › E-mail da nota: o texto do e-mail que leva a
/// nota ao cliente, da conta ou personalizado por empresa (ver
/// <see cref="RegrasModeloEmailNota"/>). Qualquer usuário lê; só o
/// administrador grava. Ocultar o convite do rodapé é do plano Escritório
/// (mesma regra da logomarca).
/// </summary>
public sealed class ModeloEmailNotaService : IModeloEmailNotaUseCase
{
    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IEmailQueueService _emailQueue;
    private readonly PlataformaOptions _plataforma;

    public ModeloEmailNotaService(
        AppDbContext db,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        IAuditLogWriter auditLogWriter,
        IEmailQueueService emailQueue,
        IOptions<PlataformaOptions> plataforma)
    {
        _db = db;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _auditLogWriter = auditLogWriter;
        _emailQueue = emailQueue;
        _plataforma = plataforma.Value;
    }

    public async Task<ModeloEmailNotaResponse> ObterAsync(Guid? empresaId, CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = Identificar();

        var empresas = await _db.Empresas.AsNoTracking()
            .OrderBy(e => e.NomeFantasia ?? e.RazaoSocial)
            .Select(e => new EmpresaModeloEmailNota(
                e.Id, e.NomeFantasia ?? e.RazaoSocial, e.Email,
                _db.ModelosEmailNota.Any(m => m.EmpresaId == e.Id)))
            .ToListAsync(cancellationToken);

        EmpresaModeloEmailNota? empresa = null;
        if (empresaId is { } id)
            empresa = empresas.FirstOrDefault(e => e.Id == id)
                ?? throw new RecursoNaoEncontradoException($"Empresa {id} não encontrada.");

        var daConta = await _db.ModelosEmailNota.AsNoTracking().FirstOrDefaultAsync(m => m.EmpresaId == null, cancellationToken);
        var daEmpresa = empresaId is null
            ? null
            : await _db.ModelosEmailNota.AsNoTracking().FirstOrDefaultAsync(m => m.EmpresaId == empresaId, cancellationToken);

        var podeOcultar = await PodeOcultarDivulgacaoAsync(tenantId, usuarioId, cancellationToken);
        var mostrado = daEmpresa ?? daConta;

        return new ModeloEmailNotaResponse(
            empresaId,
            Personalizado: empresaId is null ? daConta is not null : daEmpresa is not null,
            mostrado?.Assunto ?? RegrasModeloEmailNota.AssuntoPadrao,
            mostrado?.Mensagem ?? RegrasModeloEmailNota.MensagemPadrao,
            mostrado?.ResponderPara,
            mostrado?.CopiaOcultaPara,
            podeOcultar && (daConta?.OcultarDivulgacao ?? false),
            podeOcultar,
            await EhAdministradorAsync(tenantId, usuarioId, cancellationToken),
            empresa?.Email,
            // Na prévia, [Empresa] mostra a empresa de verdade (a escolhida, ou a primeira).
            RegrasModeloEmailNota.Campos
                .Select(c => c.Etiqueta == RegrasModeloEmailNota.CampoEmpresa && (empresa ?? empresas.FirstOrDefault()) is { } nome
                    ? c with { Exemplo = nome.Nome }
                    : c)
                .ToList(),
            empresas);
    }

    public async Task<ModeloEmailNotaResponse> SalvarAsync(SalvarModeloEmailNotaRequest request, CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = Identificar();
        if (!await EhAdministradorAsync(tenantId, usuarioId, cancellationToken))
            throw new RegraNegocioException("Só o administrador da conta muda o e-mail da nota.");

        if (request.EmpresaId is { } empresaId
            && !await _db.Empresas.AnyAsync(e => e.Id == empresaId, cancellationToken))
            throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");

        var modelo = await _db.ModelosEmailNota.FirstOrDefaultAsync(m => m.EmpresaId == request.EmpresaId, cancellationToken);

        // Empresa que volta a usar o modelo da conta.
        if (request.EmpresaId is not null && !request.Personalizar)
        {
            if (modelo is not null)
            {
                _db.ModelosEmailNota.Remove(modelo);
                _auditLogWriter.Registrar("SalvarModeloEmailNota", "Empresa", request.EmpresaId, new { personalizado = false });
                await _db.SaveChangesAsync(cancellationToken);
            }
            return await ObterAsync(request.EmpresaId, cancellationToken);
        }

        var erros = RegrasModeloEmailNota.Validar(request.Assunto, request.Mensagem, request.ResponderPara, request.CopiaOcultaPara);
        if (erros.Count > 0)
            throw new RegraNegocioException(string.Join(" ", erros));

        if (modelo is null)
        {
            modelo = new ModeloEmailNota { EmpresaId = request.EmpresaId };
            _db.ModelosEmailNota.Add(modelo);
        }

        modelo.Assunto = request.Assunto!.Trim();
        modelo.Mensagem = request.Mensagem!.Replace("\r\n", "\n").Trim();
        modelo.ResponderPara = Limpar(request.ResponderPara);
        modelo.CopiaOcultaPara = Limpar(request.CopiaOcultaPara);
        // Rodapé é do plano e vale para a conta inteira.
        modelo.OcultarDivulgacao = request.EmpresaId is null
            && request.OcultarDivulgacao
            && await PodeOcultarDivulgacaoAsync(tenantId, usuarioId, cancellationToken);

        _auditLogWriter.Registrar("SalvarModeloEmailNota", request.EmpresaId is null ? "Tenant" : "Empresa", request.EmpresaId ?? tenantId,
            new { modelo.Assunto, modelo.ResponderPara, modelo.CopiaOcultaPara, modelo.OcultarDivulgacao });
        await _db.SaveChangesAsync(cancellationToken);

        return await ObterAsync(request.EmpresaId, cancellationToken);
    }

    public async Task<string> EnviarTesteAsync(EnviarTesteModeloEmailNotaRequest request, CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = Identificar();

        var erros = RegrasModeloEmailNota.Validar(request.Assunto, request.Mensagem, null, null);
        if (erros.Count > 0)
            throw new RegraNegocioException(string.Join(" ", erros));

        var email = await _db.Users.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(email))
            throw new RegraNegocioException("Seu usuário não tem e-mail para receber o teste.");

        var empresa = request.EmpresaId is { } empresaId
            ? await _db.Empresas.AsNoTracking().Where(e => e.Id == empresaId)
                .Select(e => new { Nome = e.NomeFantasia ?? e.RazaoSocial, e.RazaoSocial, e.Email }).FirstOrDefaultAsync(cancellationToken)
            : await _db.Empresas.AsNoTracking().OrderBy(e => e.CreatedAt)
                .Select(e => new { Nome = e.NomeFantasia ?? e.RazaoSocial, e.RazaoSocial, e.Email }).FirstOrDefaultAsync(cancellationToken);
        var nomeEmpresa = empresa?.Nome ?? "Sua Empresa";

        var exemplo = new DadosEmailNota(
            "Padaria Sol Nascente", nomeEmpresa, empresa?.RazaoSocial ?? nomeEmpresa, empresa?.Email,
            "58", new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1), 344.21m, DateTimeOffset.UtcNow,
            "Manutenção do sistema", "2026/0001", ChaveAcesso: null, Homologacao: false);

        var efetivo = request.EmpresaId is { } id
            ? await ResolverAsync(tenantId, id, cancellationToken)
            : RegrasModeloEmailNota.Resolver(null,
                await _db.ModelosEmailNota.AsNoTracking().FirstOrDefaultAsync(m => m.EmpresaId == null, cancellationToken),
                await PodeOcultarDivulgacaoAsync(tenantId, usuarioId, cancellationToken));

        var assunto = "[Teste do modelo] " + RegrasModeloEmailNota.MontarAssunto(request.Assunto!, exemplo);
        var corpo = "<p style=\"color:#b45309\"><strong>Teste do modelo do e-mail da nota, com dados de exemplo e sem anexos.</strong></p>"
            + RegrasModeloEmailNota.MontarCorpoHtml(request.Mensagem!, exemplo, !efetivo.OcultarDivulgacao, EnviarNfsePorEmailUseCase.LinkDivulgacao);

        await _emailQueue.EnfileirarAsync(
            new EmailParaEnfileirar(TipoEmailCatalogo.NfseTomador, usuarioId, email, assunto, corpo,
                ResponderPara: efetivo.ResponderPara ?? empresa?.Email),
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return email;
    }

    public async Task<ModeloEmailNotaEfetivo> ResolverAsync(Guid tenantId, Guid empresaId, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters + TenantId explícito: também roda fora de uma
        // requisição (envio automático depois da autorização, jobs).
        var modelos = await _db.ModelosEmailNota.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.TenantId == tenantId && (m.EmpresaId == null || m.EmpresaId == empresaId))
            .ToListAsync(cancellationToken);
        var perfil = await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Perfil).FirstOrDefaultAsync(cancellationToken);

        return RegrasModeloEmailNota.Resolver(
            modelos.FirstOrDefault(m => m.EmpresaId == empresaId),
            modelos.FirstOrDefault(m => m.EmpresaId == null),
            RegrasLogoConta.Disponivel(perfil, operadorPlataforma: false));
    }

    private async Task<bool> PodeOcultarDivulgacaoAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken)
    {
        var perfil = await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Perfil).FirstAsync(cancellationToken);
        return RegrasLogoConta.Disponivel(perfil, _plataforma.UsuariosOperadoresIds.Contains(usuarioId));
    }

    private static string? Limpar(string? email) => string.IsNullOrWhiteSpace(email) ? null : email.Trim();

    private (Guid TenantId, Guid UsuarioId) Identificar() =>
        _currentTenant.TenantId is { } t && _currentUser.UserId is { } u
            ? (t, u)
            : throw new InvalidOperationException("Conta ou usuário não identificado.");

    private async Task<bool> EhAdministradorAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken)
    {
        var grupoId = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == usuarioId && u.TenantId == tenantId)
            .Select(u => u.GrupoId)
            .FirstOrDefaultAsync(cancellationToken);
        return grupoId is { } id && await _db.Grupos.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(g => g.Id == id && g.TenantId == tenantId && g.EhAdministrador, cancellationToken);
    }
}
