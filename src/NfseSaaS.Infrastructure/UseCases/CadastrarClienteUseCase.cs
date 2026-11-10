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

        var uf = await ConferenciaMunicipio.UfConferidaAsync(_db, EnderecoCliente.Normalizar(request.CodigoMunicipio), request.Uf, cancellationToken);

        var apelido = request.Apelido.Trim();
        await ApelidoCliente.GarantirUnicoAsync(_db, request.EmpresaId, apelido, clienteIdIgnorado: null, cancellationToken);

        var cliente = new Cliente
        {
            EmpresaId = request.EmpresaId,
            CpfCnpj = request.CpfCnpj,
            Nome = request.Nome,
            Apelido = apelido,
            Email = request.Email,
            Telefone = request.Telefone,
            CodigoMunicipio = EnderecoCliente.Normalizar(request.CodigoMunicipio),
            Cep = EnderecoCliente.Normalizar(request.Cep),
            Logradouro = EnderecoCliente.Normalizar(request.Logradouro),
            Numero = EnderecoCliente.Normalizar(request.Numero),
            Complemento = string.IsNullOrWhiteSpace(request.Complemento) ? null : request.Complemento.Trim(),
            Bairro = EnderecoCliente.Normalizar(request.Bairro),
            Uf = uf,
            RetemIss = request.RetemIss,
            EnviarNfsePorEmail = request.EnviarNfsePorEmail,
            EmailsCopia = EmailsCopiaCliente.Juntar(request.EmailsCopia),
            CopiaOculta = request.CopiaOculta
        };

        _db.Clientes.Add(cliente);

        _auditLogWriter.Registrar("CadastrarCliente", "Cliente", cliente.Id, new { cliente.EmpresaId, cliente.CpfCnpj, cliente.Nome, cliente.Apelido });

        await _db.SaveChangesAsync(cancellationToken);

        return cliente.Id;
    }
}
