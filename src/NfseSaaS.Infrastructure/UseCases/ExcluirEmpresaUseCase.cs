using Microsoft.EntityFrameworkCore;
using Npgsql;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Empresas;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Exclusão REAL (DELETE FROM) de Empresa — diferente de
/// DesativarEmpresaUseCase (soft delete, Ativo=false). Só permitida quando
/// a Empresa não tem NENHUM vínculo: nem Cliente, nem Servico, nem Nfse
/// (qualquer status, inclusive Rejeitada — uma Nfse rejeitada ainda
/// consumiu um número de DPS na sequência da Empresa, apagar a Empresa
/// deixaria esse número órfão sem explicação numa auditoria).
///
/// O COUNT() abaixo é só para dar uma mensagem de erro legível ANTES de
/// tentar — a garantia de verdade é a FK real (ON DELETE RESTRICT, ver
/// ClienteConfiguration/ServicoConfiguration/NfseConfiguration). Por isso
/// o catch: se outra requisição inserir um Cliente/Servico/Nfse bem no
/// meio da janela entre o COUNT e o SaveChanges, o COUNT não pega isso
/// (condição de corrida), mas a FK no banco pega — e aqui só traduzimos
/// esse erro de baixo nível pra a mesma exceção de negócio de sempre, em
/// vez de deixar um 500 cru estourar.
/// </summary>
public sealed class ExcluirEmpresaUseCase : IExcluirEmpresaUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public ExcluirEmpresaUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task ExecutarAsync(Guid id, CancellationToken cancellationToken)
    {
        var empresa = await _db.Empresas.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Empresa {id} não encontrada.");

        var totalClientes = await _db.Clientes.CountAsync(c => c.EmpresaId == id, cancellationToken);
        var totalServicos = await _db.Servicos.CountAsync(s => s.EmpresaId == id, cancellationToken);
        var totalNfse = await _db.NotasFiscais.CountAsync(n => n.EmpresaId == id, cancellationToken);

        if (totalClientes > 0 || totalServicos > 0 || totalNfse > 0)
        {
            throw new RegraNegocioException(
                $"Não é possível excluir a Empresa: existem {totalClientes} cliente(s), {totalServicos} serviço(s) e {totalNfse} nfse(s) vinculados. Desative a Empresa em vez de excluir.");
        }

        _db.Empresas.Remove(empresa);

        _auditLogWriter.Registrar("ExcluirEmpresa", "Empresa", empresa.Id, new { empresa.Cnpj, empresa.RazaoSocial });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            throw new RegraNegocioException(
                "Não é possível excluir a Empresa: um registro passou a referenciá-la entre a checagem e a exclusão. Tente novamente.");
        }
    }
}
