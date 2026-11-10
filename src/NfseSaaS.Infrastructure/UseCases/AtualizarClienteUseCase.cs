using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class AtualizarClienteUseCase : IAtualizarClienteUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public AtualizarClienteUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task ExecutarAsync(Guid id, AtualizarClienteRequest request, CancellationToken cancellationToken)
    {
        var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (cliente is null)
            throw new RecursoNaoEncontradoException($"Cliente {id} não encontrado.");

        // Endereço (C3): obrigatório para CNPJ e para quem retém o ISS. O
        // documento não vem na edição (não é editável), então a regra fica aqui.
        if (EnderecoCliente.MotivoObrigatorio(cliente.CpfCnpj, request.RetemIss) is { } motivo
            && EnderecoCliente.Vazio(request.CodigoMunicipio, request.Cep, request.Logradouro, request.Numero, request.Bairro, request.Uf, request.Complemento))
            throw new RegraNegocioException(motivo);

        var uf = await ConferenciaMunicipio.UfConferidaAsync(_db, EnderecoCliente.Normalizar(request.CodigoMunicipio), request.Uf, cancellationToken);

        var apelido = request.Apelido.Trim();
        await ApelidoCliente.GarantirUnicoAsync(_db, cliente.EmpresaId, apelido, clienteIdIgnorado: cliente.Id, cancellationToken);

        cliente.Nome = request.Nome;
        cliente.Apelido = apelido;
        cliente.Email = request.Email;
        cliente.Telefone = request.Telefone;
        cliente.CodigoMunicipio = EnderecoCliente.Normalizar(request.CodigoMunicipio);
        cliente.Cep = EnderecoCliente.Normalizar(request.Cep);
        cliente.Logradouro = EnderecoCliente.Normalizar(request.Logradouro);
        cliente.Numero = EnderecoCliente.Normalizar(request.Numero);
        cliente.Complemento = string.IsNullOrWhiteSpace(request.Complemento) ? null : request.Complemento.Trim();
        cliente.Bairro = EnderecoCliente.Normalizar(request.Bairro);
        cliente.Uf = uf;
        cliente.RetemIss = request.RetemIss;
        cliente.EnviarNfsePorEmail = request.EnviarNfsePorEmail;
        cliente.EmailsCopia = EmailsCopiaCliente.Juntar(request.EmailsCopia);
        cliente.CopiaOculta = request.CopiaOculta;

        _auditLogWriter.Registrar("AtualizarCliente", "Cliente", cliente.Id, new { cliente.Nome, cliente.Apelido });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
