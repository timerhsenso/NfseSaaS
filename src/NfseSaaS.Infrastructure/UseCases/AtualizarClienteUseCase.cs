using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Domain.Entities;
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

        // C10: "Disponível para" (nulo = não mexe).
        var empresaAnterior = cliente.EmpresaId;
        var novaEmpresa = await ResolverEmpresaAsync(cliente, request, cancellationToken);

        var apelido = request.Apelido.Trim();
        await ApelidoCliente.GarantirUnicoAsync(_db, novaEmpresa, apelido, cliente.CpfCnpj, clienteIdIgnorado: cliente.Id, cancellationToken);

        cliente.EmpresaId = novaEmpresa;
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

        if (empresaAnterior != novaEmpresa)
            _auditLogWriter.Registrar("AlterarDisponibilidadeCliente", "Cliente", cliente.Id,
                new { De = empresaAnterior, Para = novaEmpresa, DisponivelPara = RegrasClienteCompartilhado.Texto(novaEmpresa is null) });

        _auditLogWriter.Registrar("AtualizarCliente", "Cliente", cliente.Id, new { cliente.Nome, cliente.Apelido });

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Para onde o cliente vai. Exclusivo → compartilhado: não pode haver
    /// outro compartilhado com o mesmo documento. Compartilhado → exclusivo
    /// (da empresa atual da tela): só se ele não tiver nota nem contrato em
    /// outra empresa e se a empresa não tiver outro exclusivo com o mesmo
    /// documento. As notas emitidas não mudam (guardam o tomador da época).
    /// </summary>
    private async Task<Guid?> ResolverEmpresaAsync(Cliente cliente, AtualizarClienteRequest request, CancellationToken cancellationToken)
    {
        if (request.Compartilhado is not bool compartilhado || compartilhado == cliente.Compartilhado)
            return cliente.EmpresaId;

        if (compartilhado)
        {
            if (await _db.Clientes.AnyAsync(c => c.EmpresaId == null && c.CpfCnpj == cliente.CpfCnpj && c.Id != cliente.Id, cancellationToken))
                throw new RegraNegocioException(RegrasClienteCompartilhado.MensagemDocumentoCompartilhadoRepetido(cliente.CpfCnpj));
            return null;
        }

        if (request.EmpresaId is not Guid empresaId || empresaId == Guid.Empty)
            throw new RegraNegocioException(RegrasClienteCompartilhado.MensagemEmpresaParaExclusivo);

        if (!await _db.Empresas.AnyAsync(e => e.Id == empresaId, cancellationToken))
            throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");

        var notasFora = await _db.NotasFiscais.CountAsync(n => n.ClienteId == cliente.Id && n.EmpresaId != empresaId, cancellationToken);
        var contratosFora = await _db.Contratos.CountAsync(c => c.ClienteId == cliente.Id && c.EmpresaId != empresaId, cancellationToken);
        if (notasFora > 0 || contratosFora > 0)
            throw new RegraNegocioException(RegrasClienteCompartilhado.MensagemUsadoEmOutrasEmpresas(notasFora, contratosFora));

        if (await _db.Clientes.AnyAsync(c => c.EmpresaId == empresaId && c.CpfCnpj == cliente.CpfCnpj && c.Id != cliente.Id, cancellationToken))
            throw new RegraNegocioException($"Já existe um cliente com o CPF/CNPJ '{cliente.CpfCnpj}' cadastrado para esta empresa.");

        return empresaId;
    }
}
