using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Servicos;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ObterServicoPorIdUseCase : IObterServicoPorIdUseCase
{
    private readonly AppDbContext _db;

    public ObterServicoPorIdUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ServicoResponse> ExecutarAsync(Guid id, CancellationToken cancellationToken)
    {
        var servico = await _db.Servicos.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (servico is null)
            throw new RecursoNaoEncontradoException($"Serviço {id} não encontrado.");

        return new ServicoResponse(
            servico.Id, servico.EmpresaId, servico.Descricao, servico.CodigoTributacaoNacional, servico.CodigoNbs,
            servico.ValorPadrao, servico.Ativo, servico.CreatedAt, servico.UpdatedAt);
    }
}
