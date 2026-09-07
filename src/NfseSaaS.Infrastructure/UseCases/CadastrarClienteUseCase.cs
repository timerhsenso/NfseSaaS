using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class CadastrarClienteUseCase : ICadastrarClienteUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public CadastrarClienteUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<Guid> ExecutarAsync(CadastrarClienteRequest request, CancellationToken cancellationToken)
    {
        var empresaExiste = await _db.Empresas.AnyAsync(e => e.Id == request.EmpresaId, cancellationToken);
        if (!empresaExiste)
            throw new RecursoNaoEncontradoException($"Empresa {request.EmpresaId} não encontrada.");

        var jaExisteClienteComMesmoDocumento = await _db.Clientes
            .AnyAsync(c => c.EmpresaId == request.EmpresaId && c.CpfCnpj == request.CpfCnpj, cancellationToken);

        if (jaExisteClienteComMesmoDocumento)
            throw new RegraNegocioException($"Já existe um cliente com o CPF/CNPJ '{request.CpfCnpj}' cadastrado para esta empresa.");

        var cliente = new Cliente
        {
            EmpresaId = request.EmpresaId,
            CpfCnpj = request.CpfCnpj,
            Nome = request.Nome,
            Email = request.Email,
            Telefone = request.Telefone,
            CodigoMunicipio = request.CodigoMunicipio,
            Cep = request.Cep,
            Logradouro = request.Logradouro,
            Numero = request.Numero,
            Bairro = request.Bairro
        };

        _db.Clientes.Add(cliente);

        _auditLogWriter.Registrar("CadastrarCliente", "Cliente", cliente.Id, new { cliente.EmpresaId, cliente.CpfCnpj, cliente.Nome });

        await _db.SaveChangesAsync(cancellationToken);

        return cliente.Id;
    }
}
