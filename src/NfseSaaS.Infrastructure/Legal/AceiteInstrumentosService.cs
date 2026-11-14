using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.Legal;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Domain.Legal;
using NfseSaaS.Infrastructure.Certificates;
using NfseSaaS.Infrastructure.Persistence;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace NfseSaaS.Infrastructure.Legal;

/// <summary>Implementação de <see cref="IAceiteInstrumentosService"/> (JUR1).</summary>
public sealed partial class AceiteInstrumentosService : IAceiteInstrumentosService
{
    private const int TamanhoMaximoUserAgent = 500;

    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly CertificateFileStore _certificados;
    private readonly IMemoryCache _cache;

    /// <summary>Data de hoje (Brasília). Os testes trocam para simular a publicação e a vigência da 1.2.</summary>
    internal Func<DateOnly> Hoje { get; set; } = DataBrasilia.Hoje;

    public AceiteInstrumentosService(
        AppDbContext db,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        IAuditLogWriter auditLogWriter,
        IHttpContextAccessor httpContextAccessor,
        CertificateFileStore certificados,
        IMemoryCache cache)
    {
        _cache = cache;
        _db = db;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _auditLogWriter = auditLogWriter;
        _httpContextAccessor = httpContextAccessor;
        _certificados = certificados;
    }

    // =====================================================================
    // Pendências
    // =====================================================================

    public async Task<PendenciasInstrumentos> PendenciasAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        var hoje = Hoje();
        var atual = DocumentosLegais.Atual(hoje);
        if (atual.VersaoDpa is null && atual.VersaoAutorizacaoCertificado is null && atual.VersaoAutorizacaoEmissaoAutomatica is null)
            return PendenciasInstrumentos.Nenhuma;

        var usuario = await UsuarioAsync(usuarioId, cancellationToken);
        if (usuario is null || !usuario.Administrador)
            return PendenciasInstrumentos.Nenhuma with { EhAdministrador = false };

        var tenantId = usuario.TenantId;
        var aceites = await _db.AceitesInstrumentos.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.RevogadoEm == null)
            .Select(a => new { a.Tipo, a.Versao, a.EmpresaId })
            .ToListAsync(cancellationToken);

        bool Tem(TipoDocumentoLegal tipo, string? versao, Guid? empresaId) =>
            aceites.Any(a => a.Tipo == tipo && a.Versao == versao && a.EmpresaId == empresaId);

        var dpaPendente = atual.VersaoDpa is not null && !Tem(TipoDocumentoLegal.Dpa, atual.VersaoDpa, null);
        var versaoDpaVigente = DocumentosLegais.Vigente(hoje).VersaoDpa;
        var dpaObrigatorio = versaoDpaVigente is not null && !Tem(TipoDocumentoLegal.Dpa, versaoDpaVigente, null)
                             && !Tem(TipoDocumentoLegal.Dpa, atual.VersaoDpa, null);

        var empresas = await _db.Empresas.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.RazaoSocial)
            .Select(e => new { e.Id, e.RazaoSocial, e.NomeFantasia, e.Cnpj })
            .ToListAsync(cancellationToken);

        var certificados = new List<EmpresaPendenteAutorizacao>();
        if (atual.VersaoAutorizacaoCertificado is { } versaoA1)
        {
            foreach (var e in empresas.Where(e => _certificados.ExisteCertificado(e.Id)))
            {
                if (!Tem(TipoDocumentoLegal.AutorizacaoCertificado, versaoA1, e.Id))
                    certificados.Add(new EmpresaPendenteAutorizacao(e.Id, NomeEmpresa(e.NomeFantasia, e.RazaoSocial), e.Cnpj));
            }
        }

        var automaticas = new List<EmpresaPendenteAutorizacao>();
        if (atual.VersaoAutorizacaoEmissaoAutomatica is { } versaoAuto)
        {
            var noAutomatico = await _db.ConfiguracoesAutomacaoNotaMensal.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.Ativo && c.Modo == ModoAutomacaoNotaMensal.Automatico)
                .Select(c => c.EmpresaId)
                .ToListAsync(cancellationToken);
            foreach (var e in empresas.Where(e => noAutomatico.Contains(e.Id)))
            {
                if (!Tem(TipoDocumentoLegal.AutorizacaoEmissaoAutomatica, versaoAuto, e.Id))
                    automaticas.Add(new EmpresaPendenteAutorizacao(e.Id, NomeEmpresa(e.NomeFantasia, e.RazaoSocial), e.Cnpj));
            }
        }

        return new PendenciasInstrumentos(
            true,
            atual.VersaoDpa, dpaPendente, dpaObrigatorio,
            atual.VersaoAutorizacaoCertificado, certificados,
            atual.VersaoAutorizacaoEmissaoAutomatica, automaticas);
    }

    /// <summary>
    /// Roda em toda requisição (ExigeAceiteTermosFilter) depois da vigência do
    /// DPA: só a resposta "a conta já aceitou" vai para o cache, por conta e versão.
    /// </summary>
    public async Task<bool> DpaObrigatorioPendenteAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        var hoje = Hoje();
        var versaoDpaVigente = DocumentosLegais.Vigente(hoje).VersaoDpa;
        if (versaoDpaVigente is null)
            return false;
        var versaoDpaAtual = DocumentosLegais.Atual(hoje).VersaoDpa;

        var usuario = await UsuarioAsync(usuarioId, cancellationToken);
        if (usuario is null || !usuario.Administrador)
            return false;

        var chave = $"dpa-aceito:{usuario.TenantId:N}:{versaoDpaVigente}";
        if (_cache.TryGetValue(chave, out _))
            return false;

        var aceito = await _db.AceitesInstrumentos.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(a => a.TenantId == usuario.TenantId && a.Tipo == TipoDocumentoLegal.Dpa && a.RevogadoEm == null
                           && (a.Versao == versaoDpaVigente || a.Versao == versaoDpaAtual), cancellationToken);
        if (aceito)
            _cache.Set(chave, true, TimeSpan.FromMinutes(30));
        return !aceito;
    }

    // =====================================================================
    // Registrar e revogar
    // =====================================================================

    public async Task RegistrarDpaAsync(Guid usuarioId, string origem, CancellationToken cancellationToken)
    {
        var versao = DocumentosLegais.Atual(Hoje()).VersaoDpa
                     ?? throw new RegraNegocioException("O Acordo de Tratamento de Dados ainda não foi publicado.");
        var usuario = await UsuarioObrigatorioAsync(usuarioId, cancellationToken);
        if (!usuario.Administrador)
            throw new RegraNegocioException("Só o administrador da conta aceita o Acordo de Tratamento de Dados.");

        var aceite = NovoAceite(usuario, TipoDocumentoLegal.Dpa, versao, origem);
        var cnpjConta = await _db.Tenants.AsNoTracking().Where(t => t.Id == usuario.TenantId).Select(t => t.Documento).FirstOrDefaultAsync(cancellationToken);
        aceite.CnpjEmpresa = cnpjConta;
        Adicionar(aceite);
    }

    public async Task RegistrarAutorizacaoCertificadoAsync(Guid usuarioId, Guid empresaId, string origem, CancellationToken cancellationToken)
    {
        var versao = DocumentosLegais.Atual(Hoje()).VersaoAutorizacaoCertificado
                     ?? throw new RegraNegocioException("A Autorização do certificado A1 ainda não foi publicada.");
        var usuario = await UsuarioObrigatorioAsync(usuarioId, cancellationToken);
        var empresa = await EmpresaObrigatoriaAsync(usuario.TenantId, empresaId, cancellationToken);

        string? impressaoDigital = null;
        if (_certificados.ExisteCertificado(empresaId))
        {
            try
            {
                using var certificado = await _certificados.CarregarAsync(empresaId, cancellationToken, validarPeriodoDeValidade: false);
                impressaoDigital = certificado.Thumbprint;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Certificado ilegível: a autorização fica registrada sem a impressão digital.
            }
        }

        await RevogarAsync(TipoDocumentoLegal.AutorizacaoCertificado, empresaId, usuarioId, "Substituída por um novo aceite.", cancellationToken);

        var aceite = NovoAceite(usuario, TipoDocumentoLegal.AutorizacaoCertificado, versao, origem);
        aceite.EmpresaId = empresaId;
        aceite.CnpjEmpresa = empresa.Cnpj;
        aceite.CertificadoImpressaoDigital = impressaoDigital;
        Adicionar(aceite);
    }

    public async Task RegistrarAutorizacaoEmissaoAutomaticaAsync(Guid usuarioId, Guid empresaId, string origem, CancellationToken cancellationToken)
    {
        var versao = DocumentosLegais.Atual(Hoje()).VersaoAutorizacaoEmissaoAutomatica
                     ?? throw new RegraNegocioException("A Autorização de emissão automática ainda não foi publicada.");
        var usuario = await UsuarioObrigatorioAsync(usuarioId, cancellationToken);
        var empresa = await EmpresaObrigatoriaAsync(usuario.TenantId, empresaId, cancellationToken);

        var configuracao = await _db.ConfiguracoesAutomacaoNotaMensal.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.TenantId == usuario.TenantId && c.EmpresaId == empresaId)
            .Select(c => new { c.Ativo, c.Modo, c.Frequencia, c.DiaSemana, c.DiaDoMes, c.Horario })
            .FirstOrDefaultAsync(cancellationToken);

        await RevogarAsync(TipoDocumentoLegal.AutorizacaoEmissaoAutomatica, empresaId, usuarioId, "Substituída por um novo aceite.", cancellationToken);

        var aceite = NovoAceite(usuario, TipoDocumentoLegal.AutorizacaoEmissaoAutomatica, versao, origem);
        aceite.EmpresaId = empresaId;
        aceite.CnpjEmpresa = empresa.Cnpj;
        aceite.Detalhes = configuracao is null
            ? null
            : JsonSerializer.Serialize(new
            {
                configuracao.Ativo,
                Modo = configuracao.Modo.ToString(),
                Frequencia = configuracao.Frequencia.ToString(),
                DiaSemana = configuracao.DiaSemana?.ToString(),
                configuracao.DiaDoMes,
                Horario = configuracao.Horario.ToString("HH:mm")
            });
        Adicionar(aceite);
    }

    public async Task<bool> RevogarAsync(TipoDocumentoLegal tipo, Guid empresaId, Guid usuarioId, string motivo, CancellationToken cancellationToken)
    {
        var tenantId = _currentTenant.TenantId ?? _db.TenantIdOverrideDeSistema
                       ?? throw new InvalidOperationException("Conta não identificada para revogar a autorização.");
        var vigentes = await _db.AceitesInstrumentos.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && a.Tipo == tipo && a.EmpresaId == empresaId && a.RevogadoEm == null)
            .ToListAsync(cancellationToken);
        if (vigentes.Count == 0)
            return false;

        var agora = DateTimeOffset.UtcNow;
        foreach (var aceite in vigentes)
        {
            aceite.RevogadoEm = agora;
            aceite.RevogadoPorUsuarioId = usuarioId;
            aceite.MotivoRevogacao = motivo.Length > 300 ? motivo[..300] : motivo;
            _auditLogWriter.Registrar("RevogarAutorizacao", nameof(AceiteInstrumento), aceite.Id, new
            {
                Tipo = tipo.ToString(),
                aceite.Versao,
                EmpresaId = empresaId,
                Motivo = aceite.MotivoRevogacao
            });
        }
        return true;
    }

    public async Task<bool> AutorizacaoVigenteAsync(TipoDocumentoLegal tipo, Guid empresaId, CancellationToken cancellationToken)
    {
        var versao = DocumentosLegais.VersaoInstrumento(tipo, DocumentosLegais.Atual(Hoje()));
        if (versao is null)
            return false;
        return await _db.AceitesInstrumentos.AsNoTracking()
            .AnyAsync(a => a.Tipo == tipo && a.EmpresaId == empresaId && a.Versao == versao && a.RevogadoEm == null, cancellationToken);
    }

    // =====================================================================
    // Lista e comprovante
    // =====================================================================

    public async Task<IReadOnlyList<DocumentoAceitoResponse>> ListarAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        var tenantId = _currentTenant.TenantId ?? throw new InvalidOperationException("Conta não identificada.");
        var usuario = await UsuarioObrigatorioAsync(usuarioId, cancellationToken);
        Guid? somente = usuario.Administrador ? null : usuarioId;

        var emails = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .Select(u => new { u.Id, u.Email })
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? string.Empty, cancellationToken);
        var empresas = await _db.Empresas.AsNoTracking()
            .Select(e => new { e.Id, e.RazaoSocial, e.NomeFantasia })
            .ToDictionaryAsync(e => e.Id, e => NomeEmpresa(e.NomeFantasia, e.RazaoSocial), cancellationToken);

        var termos = (await _db.AceitesTermos.AsNoTracking().Where(a => somente == null || a.UsuarioId == somente).ToListAsync(cancellationToken))
            .SelectMany(a => new[]
            {
                ResponseDeTermos(a, TipoDocumentoLegal.TermosDeUso, a.VersaoTermosDeUso, emails),
                ResponseDeTermos(a, TipoDocumentoLegal.PoliticaPrivacidade, a.VersaoPoliticaPrivacidade, emails)
            });

        var instrumentos = (await _db.AceitesInstrumentos.AsNoTracking().Where(a => somente == null || a.UsuarioId == somente).ToListAsync(cancellationToken))
            .Select(a => new DocumentoAceitoResponse(
                a.Id, "instrumento", a.Tipo, DocumentosLegais.Nome(a.Tipo), a.Versao, DocumentosLegais.Url(a.Tipo, a.Versao),
                a.UsuarioEmail, a.EmpresaId is { } id && empresas.TryGetValue(id, out var nome) ? nome : null, a.CnpjEmpresa,
                a.AceitoEm, a.IpAddress, a.HashTexto, a.CertificadoImpressaoDigital, a.RevogadoEm, a.MotivoRevogacao));

        return termos.Concat(instrumentos)
            .OrderByDescending(d => d.AceitoEm)
            .ThenBy(d => d.Tipo)
            .ToList();
    }

    private static DocumentoAceitoResponse ResponseDeTermos(AceiteTermos a, TipoDocumentoLegal tipo, string versao, IReadOnlyDictionary<Guid, string> emails) =>
        new(a.Id, "termos", tipo, DocumentosLegais.Nome(tipo), versao, DocumentosLegais.Url(tipo, versao),
            emails.TryGetValue(a.UsuarioId, out var email) ? email : "(usuário excluído)", null, null,
            a.AceitoEm, a.IpAddress, null, null, null, null);

    public async Task<ArquivoGerado?> ComprovanteAsync(Guid usuarioId, string origem, Guid id, CancellationToken cancellationToken)
    {
        var lista = await ListarAsync(usuarioId, cancellationToken);
        var documentos = lista.Where(d => d.Id == id && d.Origem == origem).ToList();
        if (documentos.Count == 0)
            return null;

        var conta = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == _currentTenant.TenantId)
            .Select(t => new { t.RazaoSocial, t.Documento })
            .FirstOrDefaultAsync(cancellationToken);

        string? textoInstrumento = null;
        if (origem == "instrumento")
        {
            var d = documentos[0];
            textoInstrumento = TextosInstrumentos.Obter(d.Tipo, d.Versao)?.Html;
        }

        var pdf = GerarComprovante(documentos, conta?.RazaoSocial, conta?.Documento, textoInstrumento);
        var nome = documentos[0].Tipo switch
        {
            TipoDocumentoLegal.Dpa => "dpa",
            TipoDocumentoLegal.AutorizacaoCertificado => "autorizacao-certificado",
            TipoDocumentoLegal.AutorizacaoEmissaoAutomatica => "autorizacao-emissao-automatica",
            _ => "termos-e-politica"
        };
        return new ArquivoGerado($"comprovante-{nome}-{documentos[0].Versao}-{documentos[0].AceitoEm:yyyyMMdd}.pdf", "application/pdf", pdf);
    }

    /// <summary>PDF simples: registro do aceite e, nos instrumentos, o texto aceito com o hash para conferência.</summary>
    public static byte[] GerarComprovante(IReadOnlyList<DocumentoAceitoResponse> documentos, string? conta, string? documentoConta, string? textoHtml)
    {
        var principal = documentos[0];
        var paragrafos = textoHtml is null ? [] : ParagrafosDoHtml(textoHtml);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(9.5f));

                page.Header().Column(col =>
                {
                    col.Item().Text($"Comprovante de aceite — {DocumentosLegais.NomeServico}").FontSize(14).Bold();
                    col.Item().Text($"{DocumentosLegais.ResponsavelNome} · CNPJ {DocumentosLegais.ResponsavelCnpj}").FontSize(8).FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(4);
                    foreach (var d in documentos)
                        col.Item().Text($"{d.Nome} — versão {d.Versao}").SemiBold().FontSize(11);

                    void Linha(string rotulo, string? valor)
                    {
                        if (!string.IsNullOrWhiteSpace(valor))
                            col.Item().Text(t => { t.Span($"{rotulo}: ").SemiBold(); t.Span(valor); });
                    }

                    Linha("Conta", conta is null ? null : $"{conta} ({NfseSaaS.Application.UseCases.Importacao.RegrasImportacao.FormatarDocumento(documentoConta)})");
                    Linha("Empresa", principal.Empresa is null ? null : $"{principal.Empresa} (CNPJ {NfseSaaS.Application.UseCases.Importacao.RegrasImportacao.FormatarDocumento(principal.CnpjEmpresa)})");
                    Linha("Aceito por", principal.UsuarioEmail);
                    Linha("Data e hora (UTC)", principal.AceitoEm.UtcDateTime.ToString("dd/MM/yyyy HH:mm:ss"));
                    Linha("Endereço IP", principal.Ip);
                    Linha("Certificado (impressão digital)", principal.CertificadoImpressaoDigital);
                    Linha("Hash SHA-256 do texto aceito", principal.HashTexto);
                    Linha("Texto publicado em", $"https://{DocumentosLegais.Site}{principal.Url}");
                    if (principal.RevogadoEm is { } revogado)
                        Linha("Revogado em (UTC)", $"{revogado.UtcDateTime:dd/MM/yyyy HH:mm:ss} — {principal.MotivoRevogacao}");

                    if (paragrafos.Count > 0)
                    {
                        col.Item().PaddingTop(10).Text("Texto aceito").SemiBold().FontSize(11);
                        foreach (var (texto, titulo) in paragrafos)
                        {
                            if (titulo)
                                col.Item().PaddingTop(4).Text(texto).SemiBold();
                            else
                                col.Item().Text(texto);
                        }
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Página ");
                    t.CurrentPageNumber();
                    t.Span(" de ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    /// <summary>HTML simples dos instrumentos → parágrafos de texto (títulos marcados), para o PDF.</summary>
    public static List<(string Texto, bool Titulo)> ParagrafosDoHtml(string html)
    {
        var resultado = new List<(string, bool)>();
        foreach (Match m in BlocoHtml().Matches(html))
        {
            var tag = m.Groups[1].Value.ToLowerInvariant();
            var conteudo = m.Groups[2].Value;
            if (tag == "tr")
                conteudo = string.Join(" | ", CelulaHtml().Matches(conteudo).Select(c => c.Groups[1].Value));
            var texto = WebUtility.HtmlDecode(TagHtml().Replace(conteudo, string.Empty)).Trim();
            texto = EspacosHtml().Replace(texto, " ");
            if (texto.Length == 0)
                continue;
            resultado.Add((tag == "li" ? "• " + texto : texto, tag is "h1" or "h2" or "h3"));
        }
        return resultado;
    }

    [GeneratedRegex(@"<(h1|h2|h3|p|li|tr)[^>]*>(.*?)</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex BlocoHtml();

    [GeneratedRegex(@"<t[dh][^>]*>(.*?)</t[dh]>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CelulaHtml();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagHtml();

    [GeneratedRegex(@"\s+")]
    private static partial Regex EspacosHtml();

    // =====================================================================
    // Apoio
    // =====================================================================

    private sealed record UsuarioAceite(Guid Id, Guid TenantId, string Email, bool Administrador, string Perfil);

    private async Task<UsuarioAceite?> UsuarioAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        var usuario = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == usuarioId)
            .Select(u => new { u.Id, u.TenantId, u.Email, u.GrupoId })
            .FirstOrDefaultAsync(cancellationToken);
        if (usuario is null)
            return null;

        var grupo = usuario.GrupoId is { } grupoId
            ? await _db.Grupos.IgnoreQueryFilters().AsNoTracking()
                .Where(g => g.Id == grupoId && g.TenantId == usuario.TenantId)
                .Select(g => new { g.Nome, g.EhAdministrador })
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var administrador = grupo?.EhAdministrador == true;
        return new UsuarioAceite(usuario.Id, usuario.TenantId, usuario.Email ?? string.Empty, administrador,
            administrador ? "Administrador" : grupo?.Nome ?? "Sem grupo");
    }

    private async Task<UsuarioAceite> UsuarioObrigatorioAsync(Guid usuarioId, CancellationToken cancellationToken) =>
        await UsuarioAsync(usuarioId, cancellationToken) ?? throw new RecursoNaoEncontradoException("Usuário não encontrado.");

    private async Task<Empresa> EmpresaObrigatoriaAsync(Guid tenantId, Guid empresaId, CancellationToken cancellationToken) =>
        await _db.Empresas.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == empresaId && e.TenantId == tenantId, cancellationToken)
        ?? throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");

    private AceiteInstrumento NovoAceite(UsuarioAceite usuario, TipoDocumentoLegal tipo, string versao, string origem)
    {
        var texto = TextosInstrumentos.ObterObrigatorio(tipo, versao);
        var userAgent = _httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(userAgent))
            userAgent = null;
        else if (userAgent.Length > TamanhoMaximoUserAgent)
            userAgent = userAgent[..TamanhoMaximoUserAgent];

        return new AceiteInstrumento
        {
            TenantId = usuario.TenantId,
            UsuarioId = usuario.Id,
            UsuarioEmail = usuario.Email,
            PerfilUsuario = usuario.Perfil.Length > 100 ? usuario.Perfil[..100] : usuario.Perfil,
            Tipo = tipo,
            Versao = versao,
            HashTexto = texto.Hash,
            AceitoEm = DateTimeOffset.UtcNow,
            IpAddress = _currentUser.IpAddress,
            UserAgent = userAgent,
            Origem = origem
        };
    }

    private void Adicionar(AceiteInstrumento aceite)
    {
        _db.AceitesInstrumentos.Add(aceite);
        _auditLogWriter.Registrar("AceitarInstrumento", nameof(AceiteInstrumento), aceite.Id, new
        {
            Tipo = aceite.Tipo.ToString(),
            aceite.Versao,
            aceite.HashTexto,
            aceite.EmpresaId,
            aceite.CertificadoImpressaoDigital,
            aceite.Origem
        });
    }

    private static string NomeEmpresa(string? fantasia, string razao) =>
        string.IsNullOrWhiteSpace(fantasia) ? razao : fantasia!;
}
