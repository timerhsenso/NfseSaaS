using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.DocumentosEmpresa;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>DOC2: ver IDocumentosExigidosClienteUseCase.</summary>
public sealed class DocumentosExigidosClienteService : IDocumentosExigidosClienteUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly ICatalogoDocumentos _catalogo;

    public DocumentosExigidosClienteService(AppDbContext db, IAuditLogWriter auditLogWriter, ICatalogoDocumentos catalogo)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
        _catalogo = catalogo;
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

        var catalogo = await _catalogo.ObterAsync(cancellationToken);
        List<DocumentoAtual> atuais = new();
        IReadOnlyList<int> gerenciados;
        if (empresaId is { } emp && await _db.Empresas.AnyAsync(e => e.Id == emp, cancellationToken))
        {
            atuais = (await DocumentosEmpresaService.AtuaisParaEmailAsync(_db, new[] { emp }, catalogo, cancellationToken)).GetValueOrDefault(emp) ?? new();
            gerenciados = (await GerenciadosEmpresa.ObterAsync(_db, emp, catalogo, cancellationToken)).Tipos;
        }
        else
        {
            empresaId = null;
            gerenciados = RegrasCatalogoDocumentos.PadraoDaEmpresa(catalogo, Array.Empty<int>());
        }

        // Opções (DOC4): os documentos que a empresa gerencia, os "Outros" que
        // ela já guardou e o que o cliente já exige (mesmo que a empresa não
        // gerencie mais — continua marcado até alguém desmarcar).
        var chaves = GerenciadosEmpresa.Ordenar(gerenciados.Select(catalogo.Obter)).Select(t => new ChaveDocumento(t.Tipo, string.Empty))
            .Concat(atuais.Where(a => a.Tipo == TiposDocumentoSistema.Outro).OrderBy(a => a.Titulo, StringComparer.CurrentCultureIgnoreCase)
                .Select(a => new ChaveDocumento(a.Tipo, a.NomeChave)))
            .Concat(exigidos.Where(e => catalogo.Buscar(e.Tipo) is not null))
            .Distinct()
            .ToList();

        var avaliados = empresaId is null
            ? null
            : RegrasDocumentosExigidos.Avaliar(chaves, atuais, DataBrasilia.Hoje(), catalogo).ToDictionary(a => a.Chave);

        var opcoes = chaves.Select(c =>
        {
            var avaliado = avaliados?.GetValueOrDefault(c);
            return new OpcaoDocumentoExigido(c.Tipo, c.NomeChave,
                avaliado?.Titulo ?? RegrasDocumentosExigidos.TituloDaChave(c, catalogo),
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

        var catalogo = await _catalogo.ObterAsync(cancellationToken);
        var valor = RegrasDocumentosExigidos.Gravar(
            (request.Documentos ?? Array.Empty<ChaveDocumentoRequest>()).Select(d => new ChaveDocumento(d.Tipo, d.NomeChave ?? string.Empty)), catalogo);

        if (cliente.DocumentosExigidos == valor && cliente.DocumentoPendenteEnvioAutomatico == request.AcaoPendente)
            return;

        cliente.DocumentosExigidos = valor;
        cliente.DocumentoPendenteEnvioAutomatico = request.AcaoPendente;
        cliente.UpdatedAt = DateTimeOffset.UtcNow;
        _auditLogWriter.Registrar("AlterarDocumentosExigidosCliente", "Cliente", clienteId,
            new { documentos = RegrasDocumentosExigidos.Ler(valor).Select(c => RegrasDocumentosExigidos.TituloDaChave(c, catalogo)).ToList(), acao = request.AcaoPendente.ToString() });
        await _db.SaveChangesAsync(cancellationToken);
    }
}
