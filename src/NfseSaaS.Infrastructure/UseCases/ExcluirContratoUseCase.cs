using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Infrastructure.Documents;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Exclusão REAL de Contrato. Bloqueada se houver Nfse vinculada
/// (Nfse.ContratoId, Parte 2) OU histórico de reajuste (ReajusteContrato,
/// Parte 3) — mesma lógica de Excluir(Empresa|Cliente|Servico)UseCase.
/// ContratoServico e ContratoDocumento NÃO bloqueiam (são detalhe/anexo,
/// não histórico fiscal) — cascata no banco (FK ON DELETE CASCADE) cuida
/// das linhas; a pasta de documentos em disco é apagada aqui, depois do
/// SaveChanges ter sucesso (nunca perde arquivo de um Contrato que ainda
/// existe no banco por causa de falha no meio do caminho).
/// </summary>
public sealed class ExcluirContratoUseCase : IExcluirContratoUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly ContratoDocumentoFileStore _documentoFileStore;

    public ExcluirContratoUseCase(AppDbContext db, IAuditLogWriter auditLogWriter, ContratoDocumentoFileStore documentoFileStore)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
        _documentoFileStore = documentoFileStore;
    }

    public async Task ExecutarAsync(Guid id, CancellationToken cancellationToken)
    {
        var contrato = await _db.Contratos.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Contrato {id} não encontrado.");

        var totalNfse = await _db.NotasFiscais.CountAsync(n => n.ContratoId == id, cancellationToken);
        // O "Valor inicial" sempre existe e não conta como histórico real —
        // só reajustes/alterações bloqueiam a exclusão.
        var totalMudancasDeValor = await _db.MovimentacoesValorContrato
            .CountAsync(m => m.ContratoId == id && m.Tipo != Domain.Enums.TipoMovimentacaoValor.ValorInicial, cancellationToken);

        if (totalNfse > 0 || totalMudancasDeValor > 0)
        {
            throw new RegraNegocioException(
                $"Não é possível excluir o Contrato: existem {totalNfse} nfse(s) e {totalMudancasDeValor} mudança(s) de valor registradas. Desative o Contrato em vez de excluir.");
        }

        // FK Restrict (histórico append-only): o evento de Valor inicial
        // sai explicitamente, junto com o Contrato, na mesma gravação.
        var valorInicial = await _db.MovimentacoesValorContrato.Where(m => m.ContratoId == id).ToListAsync(cancellationToken);
        _db.MovimentacoesValorContrato.RemoveRange(valorInicial);

        _db.Contratos.Remove(contrato);

        _auditLogWriter.Registrar("ExcluirContrato", "Contrato", contrato.Id, new { contrato.Descricao });

        await _db.SaveChangesAsync(cancellationToken);

        _documentoFileStore.ExcluirPastaContrato(contrato.TenantId, contrato.EmpresaId, contrato.Id);
    }
}
