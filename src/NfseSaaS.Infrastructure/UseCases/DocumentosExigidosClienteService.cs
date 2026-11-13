using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.DocumentosEmpresa;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>DOC2: ver IDocumentosExigidosClienteUseCase.</summary>
public sealed class DocumentosExigidosClienteService : IDocumentosExigidosClienteUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public DocumentosExigidosClienteService(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<DocumentosExigidosClienteResponse> ObterAsync(Guid? clienteId, Guid? empresaId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ChaveDocumento> exigidos = Array.Empty<ChaveDocumento>();
        var acao = AcaoDocumentoPendente.MandarSemOPendente;
        if (clienteId is { } id)
        {
            var cliente = await _db.Clientes.AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new { c.DocumentosExigidos, c.DocumentoPendenteEnvioAutomatico })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new RecursoNaoEncontradoException($"Cliente {id} não encontrado.");
            exigidos = RegrasDocumentosExigidos.Ler(cliente.DocumentosExigidos);
            acao = cliente.DocumentoPendenteEnvioAutomatico;
        }

        List<DocumentoAtual> atuais = new();
        if (empresaId is { } emp && await _db.Empresas.AnyAsync(e => e.Id == emp, cancellationToken))
            atuais = (await DocumentosEmpresaService.AtuaisParaEmailAsync(_db, new[] { emp }, cancellationToken)).GetValueOrDefault(emp) ?? new();

        // Opções: tipos fixos, "Outros" que a empresa já guardou e os "Outros" já escolhidos.
        var chaves = CatalogoDocumentosEmpresa.Fixos.Select(t => new ChaveDocumento(t.Tipo, string.Empty))
            .Concat(atuais.Where(a => a.Tipo == TipoDocumentoEmpresa.Outro).OrderBy(a => a.Titulo, StringComparer.CurrentCultureIgnoreCase)
                .Select(a => new ChaveDocumento(a.Tipo, a.NomeChave)))
            .Concat(exigidos.Where(e => e.Tipo == TipoDocumentoEmpresa.Outro))
            .Distinct()
            .ToList();

        var avaliados = empresaId is null
            ? null
            : RegrasDocumentosExigidos.Avaliar(chaves, atuais, DataBrasilia.Hoje()).ToDictionary(a => a.Chave);

        var opcoes = chaves.Select(c =>
        {
            var avaliado = avaliados?.GetValueOrDefault(c);
            return new OpcaoDocumentoExigido(c.Tipo, c.NomeChave,
                avaliado?.Titulo ?? RegrasDocumentosExigidos.TituloDaChave(c),
                exigidos.Contains(c),
                avaliado?.Situacao,
                avaliado is null ? null : RegrasDocumentosExigidos.TextoSituacao(avaliado));
        }).ToList();

        return new DocumentosExigidosClienteResponse(clienteId, acao, opcoes);
    }

    public async Task SalvarAsync(Guid clienteId, SalvarDocumentosExigidosRequest request, CancellationToken cancellationToken)
    {
        var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.Id == clienteId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Cliente {clienteId} não encontrado.");

        if (!Enum.IsDefined(request.AcaoPendente))
            throw new RegraNegocioException("Escolha o que fazer quando um documento estiver vencido ou faltando.");

        var valor = RegrasDocumentosExigidos.Gravar(
            (request.Documentos ?? Array.Empty<ChaveDocumentoRequest>()).Select(d => new ChaveDocumento(d.Tipo, d.NomeChave ?? string.Empty)));

        if (cliente.DocumentosExigidos == valor && cliente.DocumentoPendenteEnvioAutomatico == request.AcaoPendente)
            return;

        cliente.DocumentosExigidos = valor;
        cliente.DocumentoPendenteEnvioAutomatico = request.AcaoPendente;
        cliente.UpdatedAt = DateTimeOffset.UtcNow;
        _auditLogWriter.Registrar("AlterarDocumentosExigidosCliente", "Cliente", clienteId,
            new { documentos = RegrasDocumentosExigidos.Ler(valor).Select(RegrasDocumentosExigidos.TituloDaChave).ToList(), acao = request.AcaoPendente.ToString() });
        await _db.SaveChangesAsync(cancellationToken);
    }
}
