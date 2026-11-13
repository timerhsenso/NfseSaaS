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
/// Cadastro "Modelos de e-mail" (ver <see cref="RegrasModeloEmailNota"/>):
/// modelos reutilizáveis do e-mail da nota, um padrão por conta, e a
/// escolha do modelo na Empresa e no Cliente. Qualquer usuário lê; só o
/// administrador cria, altera e exclui modelos. Escolher o modelo de uma
/// Empresa ou de um Cliente segue a permissão daquelas telas (controller).
/// Tirar o convite do rodapé é do plano Escritório (mesma regra da logomarca).
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
        IOptions<PlataformaOptions> plataforma,
        NfseSaaS.Application.UseCases.Recursos.IRecursosConta recursos,
        ILinkConfirmacaoRecebimento linkConfirmacao)
    {
        _recursos = recursos;
        _linkConfirmacao = linkConfirmacao;
        _db = db;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _auditLogWriter = auditLogWriter;
        _emailQueue = emailQueue;
        _plataforma = plataforma.Value;
    }

    public async Task<ModelosEmailNotaResponse> ListarAsync(CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = Identificar();
        await GarantirPadraoAsync(cancellationToken);

        var modelos = await _db.ModelosEmailNota.AsNoTracking()
            .OrderByDescending(m => m.Padrao).ThenBy(m => m.Nome)
            .ToListAsync(cancellationToken);
        var empresas = await _db.Empresas.AsNoTracking()
            .Where(e => e.ModeloEmailNotaId != null)
            .Select(e => new { e.ModeloEmailNotaId, Nome = e.NomeFantasia ?? e.RazaoSocial })
            .ToListAsync(cancellationToken);
        var clientes = await _db.Clientes.AsNoTracking()
            .Where(c => c.ModeloEmailNotaId != null)
            .GroupBy(c => c.ModeloEmailNotaId)
            .Select(g => new { ModeloId = g.Key, Quantidade = g.Count() })
            .ToListAsync(cancellationToken);

        var itens = modelos.Select(m => new ModeloEmailNotaItem(
                m.Id, m.Nome, m.Padrao, m.Assunto, m.Mensagem, m.ResponderPara, m.CopiaOcultaPara, m.OcultarDivulgacao,
                empresas.Where(e => e.ModeloEmailNotaId == m.Id).Select(e => e.Nome).OrderBy(n => n).ToList(),
                clientes.FirstOrDefault(c => c.ModeloId == m.Id)?.Quantidade ?? 0,
                m.OcultarSaidaCopias,
                m.ValidadeConfirmacaoDias))
            .ToList();

        // Na prévia, [Empresa] mostra a primeira empresa de verdade.
        var primeiraEmpresa = await _db.Empresas.AsNoTracking().OrderBy(e => e.CreatedAt)
            .Select(e => e.NomeFantasia ?? e.RazaoSocial).FirstOrDefaultAsync(cancellationToken);
        var campos = RegrasModeloEmailNota.Campos
            .Select(c => c.Etiqueta == RegrasModeloEmailNota.CampoEmpresa && primeiraEmpresa is not null ? c with { Exemplo = primeiraEmpresa } : c)
            .ToList();

        return new ModelosEmailNotaResponse(
            itens, campos,
            await EhAdministradorAsync(tenantId, usuarioId, cancellationToken),
            await PodeOcultarDivulgacaoAsync(tenantId, usuarioId, cancellationToken));
    }

    public async Task<IReadOnlyList<OpcaoModeloEmailNota>> OpcoesAsync(CancellationToken cancellationToken)
    {
        await GarantirPadraoAsync(cancellationToken);
        return await _db.ModelosEmailNota.AsNoTracking()
            .OrderByDescending(m => m.Padrao).ThenBy(m => m.Nome)
            .Select(m => new OpcaoModeloEmailNota(m.Id, m.Nome, m.Padrao))
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid> CriarAsync(SalvarModeloEmailNotaRequest request, CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = await ExigirAdministradorAsync(cancellationToken);
        await ValidarAsync(request, idAtual: null, cancellationToken);

        var quantidade = await _db.ModelosEmailNota.CountAsync(cancellationToken);
        if (quantidade >= RegrasModeloEmailNota.MaximoModelos)
            throw new RegraNegocioException($"A conta pode ter até {RegrasModeloEmailNota.MaximoModelos} modelos. Exclua um que não usa mais.");

        var modelo = new ModeloEmailNota { Padrao = quantidade == 0 };
        await PreencherAsync(modelo, request, tenantId, usuarioId, cancellationToken);
        _db.ModelosEmailNota.Add(modelo);

        _auditLogWriter.Registrar("CriarModeloEmailNota", "ModeloEmailNota", modelo.Id, new { modelo.Nome, modelo.Assunto });
        await _db.SaveChangesAsync(cancellationToken);
        return modelo.Id;
    }

    public async Task AtualizarAsync(Guid id, SalvarModeloEmailNotaRequest request, CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = await ExigirAdministradorAsync(cancellationToken);
        var modelo = await ObterAsync(id, cancellationToken);
        await ValidarAsync(request, id, cancellationToken);

        await PreencherAsync(modelo, request, tenantId, usuarioId, cancellationToken);
        _auditLogWriter.Registrar("AlterarModeloEmailNota", "ModeloEmailNota", modelo.Id,
            new { modelo.Nome, modelo.Assunto, modelo.ResponderPara, modelo.CopiaOcultaPara, modelo.OcultarDivulgacao, modelo.OcultarSaidaCopias, modelo.ValidadeConfirmacaoDias });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid> DuplicarAsync(Guid id, CancellationToken cancellationToken)
    {
        await ExigirAdministradorAsync(cancellationToken);
        var original = await ObterAsync(id, cancellationToken);
        if (await _db.ModelosEmailNota.CountAsync(cancellationToken) >= RegrasModeloEmailNota.MaximoModelos)
            throw new RegraNegocioException($"A conta pode ter até {RegrasModeloEmailNota.MaximoModelos} modelos. Exclua um que não usa mais.");

        var nomes = await _db.ModelosEmailNota.Select(m => m.Nome).ToListAsync(cancellationToken);
        var copia = new ModeloEmailNota
        {
            Nome = NomeLivre("Cópia de " + original.Nome, nomes),
            Assunto = original.Assunto,
            Mensagem = original.Mensagem,
            ResponderPara = original.ResponderPara,
            CopiaOcultaPara = original.CopiaOcultaPara,
            OcultarDivulgacao = original.OcultarDivulgacao,
            OcultarSaidaCopias = original.OcultarSaidaCopias,
            ValidadeConfirmacaoDias = original.ValidadeConfirmacaoDias
        };
        _db.ModelosEmailNota.Add(copia);
        _auditLogWriter.Registrar("CriarModeloEmailNota", "ModeloEmailNota", copia.Id, new { copia.Nome, DuplicadoDe = original.Id });
        await _db.SaveChangesAsync(cancellationToken);
        return copia.Id;
    }

    public async Task DefinirPadraoAsync(Guid id, CancellationToken cancellationToken)
    {
        await ExigirAdministradorAsync(cancellationToken);
        var modelo = await ObterAsync(id, cancellationToken);
        if (modelo.Padrao)
            return;

        // Dois passos numa transação: o índice único de "um padrão por
        // conta" não deixa existir dois ao mesmo tempo nem por um instante.
        var estrategia = _db.Database.CreateExecutionStrategy();
        await estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await _db.Database.BeginTransactionAsync(cancellationToken);
            await _db.ModelosEmailNota.Where(m => m.Padrao).ExecuteUpdateAsync(s => s.SetProperty(m => m.Padrao, false), cancellationToken);
            modelo.Padrao = true;
            _auditLogWriter.Registrar("PadraoModeloEmailNota", "ModeloEmailNota", modelo.Id, new { modelo.Nome });
            await _db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        });
    }

    public async Task ExcluirAsync(Guid id, CancellationToken cancellationToken)
    {
        await ExigirAdministradorAsync(cancellationToken);
        var modelo = await ObterAsync(id, cancellationToken);
        if (modelo.Padrao)
            throw new RegraNegocioException("O modelo padrão não pode ser excluído. Marque outro como padrão antes.");

        // FK SET NULL no banco: empresas e clientes que usavam voltam ao padrão.
        _db.ModelosEmailNota.Remove(modelo);
        _auditLogWriter.Registrar("ExcluirModeloEmailNota", "ModeloEmailNota", modelo.Id, new { modelo.Nome });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> EnviarTesteAsync(EnviarTesteModeloEmailNotaRequest request, CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = Identificar();

        var erros = RegrasModeloEmailNota.Validar(request.Assunto, request.Mensagem, request.ResponderPara, null);
        if (erros.Count > 0)
            throw new RegraNegocioException(string.Join(" ", erros));

        var email = await _db.Users.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(email))
            throw new RegraNegocioException("Seu usuário não tem e-mail para receber o teste.");

        var empresa = await _db.Empresas.AsNoTracking().OrderBy(e => e.CreatedAt)
            .Select(e => new { Nome = e.NomeFantasia ?? e.RazaoSocial, e.RazaoSocial, e.Email })
            .FirstOrDefaultAsync(cancellationToken);
        var nomeEmpresa = empresa?.Nome ?? "Sua Empresa";

        var exemplo = new DadosEmailNota(
            "Padaria Sol Nascente", nomeEmpresa, empresa?.RazaoSocial ?? nomeEmpresa, empresa?.Email,
            "58", new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1), 344.21m, DateTimeOffset.UtcNow,
            "Manutenção do sistema", "2026/0001", ChaveAcesso: null, Homologacao: false);

        var ocultar = request.OcultarDivulgacao && await PodeOcultarDivulgacaoAsync(tenantId, usuarioId, cancellationToken);
        var assunto = "[Teste do modelo] " + RegrasModeloEmailNota.MontarAssunto(request.Assunto!, exemplo);
        var corpo = "<p style=\"color:#b45309\"><strong>Teste do modelo do e-mail da nota, com dados de exemplo e sem anexos.</strong></p>"
            // CR1: no teste, o [Confirmar recebimento] leva à página de exemplo (não confirma nada).
            + RegrasModeloEmailNota.MontarCorpoHtml(request.Mensagem!, exemplo, !ocultar, EnviarNfsePorEmailUseCase.LinkDivulgacao,
                linkConfirmacao: _linkConfirmacao.Exemplo);

        var responderPara = string.IsNullOrWhiteSpace(request.ResponderPara) ? empresa?.Email : request.ResponderPara.Trim();
        await _emailQueue.EnfileirarAsync(
            new EmailParaEnfileirar(TipoEmailCatalogo.NfseTomador, usuarioId, email, assunto, corpo,
                ResponderPara: string.IsNullOrWhiteSpace(responderPara) ? null : responderPara),
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return email;
    }

    public async Task DefinirDaEmpresaAsync(Guid empresaId, DefinirModeloEmailNotaRequest request, CancellationToken cancellationToken)
    {
        var empresa = await _db.Empresas.FirstOrDefaultAsync(e => e.Id == empresaId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");
        await GarantirModeloDaContaAsync(request.ModeloId, cancellationToken);
        if (empresa.ModeloEmailNotaId == request.ModeloId)
            return;

        empresa.ModeloEmailNotaId = request.ModeloId;
        _auditLogWriter.Registrar("DefinirModeloEmailNota", "Empresa", empresaId, new { request.ModeloId });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DefinirDoClienteAsync(Guid clienteId, DefinirModeloEmailNotaRequest request, CancellationToken cancellationToken)
    {
        var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.Id == clienteId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Cliente {clienteId} não encontrado.");
        await GarantirModeloDaContaAsync(request.ModeloId, cancellationToken);
        if (cliente.ModeloEmailNotaId == request.ModeloId)
            return;

        cliente.ModeloEmailNotaId = request.ModeloId;
        _auditLogWriter.Registrar("DefinirModeloEmailNota", "Cliente", clienteId, new { request.ModeloId });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ModeloEmailNotaEfetivo> ResolverAsync(
        Guid tenantId, Guid empresaId, Guid clienteId, Guid? modeloEscolhidoId, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters + TenantId explícito: também roda fora de uma
        // requisição (envio automático depois da autorização, jobs).
        var doCliente = await _db.Clientes.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Id == clienteId && c.TenantId == tenantId).Select(c => c.ModeloEmailNotaId).FirstOrDefaultAsync(cancellationToken);
        var daEmpresa = await _db.Empresas.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.Id == empresaId && e.TenantId == tenantId).Select(e => e.ModeloEmailNotaId).FirstOrDefaultAsync(cancellationToken);

        var modelos = await _db.ModelosEmailNota.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.TenantId == tenantId
                && (m.Padrao || m.Id == modeloEscolhidoId || m.Id == doCliente || m.Id == daEmpresa))
            .ToListAsync(cancellationToken);
        // PL1: recurso "email-sem-divulgacao" do plano (padrão: contas de contador).
        var podeOcultar = await _recursos.LigadoAsync(tenantId, NfseSaaS.Application.UseCases.Recursos.CatalogoRecursos.EmailSemDivulgacao, cancellationToken);

        if (modeloEscolhidoId is { } escolhido)
            return RegrasModeloEmailNota.Efetivo(
                modelos.FirstOrDefault(m => m.Id == escolhido)
                    ?? throw new RecursoNaoEncontradoException($"Modelo de e-mail {escolhido} não encontrado."),
                podeOcultar);

        return RegrasModeloEmailNota.Resolver(
            modelos.FirstOrDefault(m => m.Id == doCliente),
            modelos.FirstOrDefault(m => m.Id == daEmpresa),
            modelos.FirstOrDefault(m => m.Padrao),
            podeOcultar);
    }

    /// <summary>Conta sem modelo padrão ganha o "Padrão" com o texto do sistema.</summary>
    private async Task GarantirPadraoAsync(CancellationToken cancellationToken)
    {
        if (await _db.ModelosEmailNota.AnyAsync(m => m.Padrao, cancellationToken))
            return;

        var nomes = await _db.ModelosEmailNota.Select(m => m.Nome).ToListAsync(cancellationToken);
        _db.ModelosEmailNota.Add(new ModeloEmailNota
        {
            Nome = NomeLivre(RegrasModeloEmailNota.NomeModeloInicial, nomes),
            Padrao = true,
            Assunto = RegrasModeloEmailNota.AssuntoPadrao,
            Mensagem = RegrasModeloEmailNota.MensagemPadrao
        });
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Duas abas abrindo ao mesmo tempo: o outro pedido já criou.
            _db.ChangeTracker.Clear();
        }
    }

    private async Task ValidarAsync(SalvarModeloEmailNotaRequest request, Guid? idAtual, CancellationToken cancellationToken)
    {
        var erros = RegrasModeloEmailNota.ValidarModelo(request.Nome, request.Assunto, request.Mensagem, request.ResponderPara, request.CopiaOcultaPara).ToList();
        var nome = request.Nome?.Trim();
        if (!string.IsNullOrEmpty(nome)
            && await _db.ModelosEmailNota.AnyAsync(m => m.Nome.ToLower() == nome.ToLower() && m.Id != idAtual, cancellationToken))
            erros.Add($"Já existe um modelo chamado \"{nome}\".");
        if (erros.Count > 0)
            throw new RegraNegocioException(string.Join(" ", erros));
    }

    private async Task PreencherAsync(ModeloEmailNota modelo, SalvarModeloEmailNotaRequest request, Guid tenantId, Guid usuarioId, CancellationToken cancellationToken)
    {
        modelo.Nome = request.Nome!.Trim();
        modelo.Assunto = request.Assunto!.Trim();
        modelo.Mensagem = request.Mensagem!.Replace("\r\n", "\n").Trim();
        modelo.ResponderPara = Limpar(request.ResponderPara);
        modelo.CopiaOcultaPara = Limpar(request.CopiaOcultaPara);
        modelo.OcultarDivulgacao = request.OcultarDivulgacao && await PodeOcultarDivulgacaoAsync(tenantId, usuarioId, cancellationToken);
        modelo.OcultarSaidaCopias = request.OcultarSaidaCopias;
        if (request.ValidadeConfirmacaoDias is { } dias)
            modelo.ValidadeConfirmacaoDias = RegrasConfirmacaoRecebimento.ValidarDias(dias);
    }

    private async Task<ModeloEmailNota> ObterAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.ModelosEmailNota.FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Modelo de e-mail {id} não encontrado.");

    private async Task GarantirModeloDaContaAsync(Guid? modeloId, CancellationToken cancellationToken)
    {
        if (modeloId is { } id && !await _db.ModelosEmailNota.AnyAsync(m => m.Id == id, cancellationToken))
            throw new RecursoNaoEncontradoException($"Modelo de e-mail {id} não encontrado.");
    }

    internal static string NomeLivre(string desejado, IReadOnlyCollection<string> existentes)
    {
        var baseNome = desejado.Length > ModeloEmailNota.TamanhoMaximoNome - 4 ? desejado[..(ModeloEmailNota.TamanhoMaximoNome - 4)] : desejado;
        var nome = baseNome;
        for (var i = 2; existentes.Any(e => string.Equals(e, nome, StringComparison.OrdinalIgnoreCase)); i++)
            nome = $"{baseNome} ({i})";
        return nome;
    }

    private async Task<bool> PodeOcultarDivulgacaoAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken) =>
        await _recursos.LigadoAsync(tenantId, NfseSaaS.Application.UseCases.Recursos.CatalogoRecursos.EmailSemDivulgacao, cancellationToken)
        || _plataforma.UsuariosOperadoresIds.Contains(usuarioId);

    private readonly NfseSaaS.Application.UseCases.Recursos.IRecursosConta _recursos;
    private readonly ILinkConfirmacaoRecebimento _linkConfirmacao;

    private static string? Limpar(string? email) => string.IsNullOrWhiteSpace(email) ? null : email.Trim();

    private async Task<(Guid TenantId, Guid UsuarioId)> ExigirAdministradorAsync(CancellationToken cancellationToken)
    {
        var (tenantId, usuarioId) = Identificar();
        if (!await EhAdministradorAsync(tenantId, usuarioId, cancellationToken))
            throw new RegraNegocioException("Só o administrador da conta cria e altera os modelos de e-mail.");
        return (tenantId, usuarioId);
    }

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
