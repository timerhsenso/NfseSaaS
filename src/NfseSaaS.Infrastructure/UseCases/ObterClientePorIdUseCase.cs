using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ObterClientePorIdUseCase : IObterClientePorIdUseCase
{
    private readonly AppDbContext _db;

    public ObterClientePorIdUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ClienteResponse> ExecutarAsync(Guid id, CancellationToken cancellationToken)
    {
        var cliente = await _db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (cliente is null)
            throw new RecursoNaoEncontradoException($"Cliente {id} não encontrado.");

        return new ClienteResponse(
            cliente.Id, cliente.EmpresaId, cliente.CpfCnpj, cliente.Nome, cliente.Email, cliente.Telefone,
            cliente.CodigoMunicipio, cliente.Cep, cliente.Logradouro, cliente.Numero, cliente.Complemento,
            cliente.Bairro, cliente.Uf, cliente.Ativo, cliente.CreatedAt, cliente.UpdatedAt);
    }
}
