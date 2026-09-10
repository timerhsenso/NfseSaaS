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
/// ou excluído depois.
///
/// JÁ NÃO é sempre seguro, porém: Contrato tem uma FK real pra Servico
/// (herda a classificação fiscal de lá) — um Servico usado por algum
/// Contrato não pode ser excluído sem antes o Contrato apontar pra outro
/// Servico ou ser removido.
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

        var totalContratos = await _db.Contratos.CountAsync(c => c.ServicoId == id, cancellationToken);
        if (totalContratos > 0)
        {
            throw new RegraNegocioException(
                $"Não é possível excluir o Serviço: existem {totalContratos} contrato(s) vinculados. Desative o Serviço em vez de excluir.");
        }

        _db.Servicos.Remove(servico);

        _auditLogWriter.Registrar("ExcluirServico", "Servico", servico.Id, new { servico.Descricao });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
