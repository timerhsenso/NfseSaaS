using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Servicos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Exclusão REAL de Servico — sem checagem de vínculo com Nfse, DE
/// PROPÓSITO: a entidade Nfse não guarda ServicoId. No momento da emissão,
/// Descricao/Valor são copiados do Servico para dentro da própria Nfse
/// (ver EmitirNfseUseCase) — decisão deliberada para que o histórico
/// fiscal nunca mude retroativamente se o catálogo de Servico for editado
/// ou excluído depois. Por isso excluir um Servico já usado em emissões
/// passadas é seguro e não precisa (nem deve) ser bloqueado.
/// </summary>
public sealed class ExcluirServicoUseCase : IExcluirServicoUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public ExcluirServicoUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task ExecutarAsync(Guid id, CancellationToken cancellationToken)
    {
        var servico = await _db.Servicos.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Serviço {id} não encontrado.");

        _db.Servicos.Remove(servico);

        _auditLogWriter.Registrar("ExcluirServico", "Servico", servico.Id, new { servico.Descricao });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
