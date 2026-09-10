using Microsoft.EntityFrameworkCore;
using Npgsql;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Exclusão REAL de Cliente. Só permitida se não houver NENHUMA Nfse
/// vinculada (qualquer status). O COUNT() é só para a mensagem de erro
/// legível — a FK real (ON DELETE RESTRICT, ver NfseConfiguration) é quem
/// garante de verdade e fecha a janela de corrida entre o COUNT e o
/// SaveChanges (ver comentário completo em ExcluirEmpresaUseCase).
/// </summary>
public sealed class ExcluirClienteUseCase : IExcluirClienteUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public ExcluirClienteUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task ExecutarAsync(Guid id, CancellationToken cancellationToken)
    {
        var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Cliente {id} não encontrado.");

        var totalNfse = await _db.NotasFiscais.CountAsync(n => n.ClienteId == id, cancellationToken);
        var totalContratos = await _db.Contratos.CountAsync(c => c.ClienteId == id, cancellationToken);

        if (totalNfse > 0 || totalContratos > 0)
        {
            throw new RegraNegocioException(
                $"Não é possível excluir o Cliente: existem {totalNfse} nfse(s) e {totalContratos} contrato(s) vinculados. Desative o Cliente em vez de excluir.");
        }

        _db.Clientes.Remove(cliente);

        _auditLogWriter.Registrar("ExcluirCliente", "Cliente", cliente.Id, new { cliente.CpfCnpj, cliente.Nome });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            throw new RegraNegocioException(
                "Não é possível excluir o Cliente: uma Nfse passou a referenciá-lo entre a checagem e a exclusão. Tente novamente.");
        }
    }
}
