using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.Fiscal.Retencoes;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Application.UseCases.Recursos;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class CadastrarClienteUseCase : ICadastrarClienteUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly ICurrentTenant _currentTenant;
    private readonly IRecursosConta _recursos;

    public CadastrarClienteUseCase(AppDbContext db, IAuditLogWriter auditLogWriter, ICurrentTenant currentTenant, IRecursosConta recursos)
    {
        _recursos = recursos;
        _db = db;
        _auditLogWriter = auditLogWriter;
        _currentTenant = currentTenant;
    }

    public async Task<Guid> ExecutarAsync(CadastrarClienteRequest request, CancellationToken cancellationToken)
    {
        var empresaExiste = await _db.Empresas.AnyAsync(e => e.Id == request.EmpresaId, cancellationToken);
        if (!empresaExiste)
            throw new RecursoNaoEncontradoException($"Empresa {request.EmpresaId} não encontrada.");

        // PL1: limite de clientes por empresa do plano (ativos que a empresa
        // enxerga, compartilhados incluídos). Só barra cadastro, nunca emissão.
        if (_currentTenant.TenantId is { } tenantId)
        {
            var ativos = await ClientesDaEmpresa.Visiveis(_db, request.EmpresaId).CountAsync(c => c.Ativo, cancellationToken);
            await _recursos.GarantirCabeMaisUmAsync(tenantId, CatalogoRecursos.ClientesPorEmpresa, ativos, cancellationToken);
        }

        // C10: "Disponível para". Sem escolha na tela, vale o padrão da conta.
        var disponibilidade = await ObterDisponibilidadeClienteUseCase.CalcularAsync(_db, _currentTenant.TenantId, cancellationToken);
        var compartilhado = request.Compartilhado ?? disponibilidade.PadraoCompartilhado;
        Guid? empresaDoCliente = compartilhado ? null : request.EmpresaId;

        if (compartilhado)
        {
            if (await _db.Clientes.AnyAsync(c => c.EmpresaId == null && c.CpfCnpj == request.CpfCnpj, cancellationToken))
                throw new RegraNegocioException(RegrasClienteCompartilhado.MensagemDocumentoCompartilhadoRepetido(request.CpfCnpj));

            // Um compartilhado novo ficaria escondido na própria empresa da
            // tela pelo exclusivo que ela já tem: o usuário "salvaria" e não
            // veria nada. Melhor mandar mudar o cadastro que já existe.
            if (await _db.Clientes.AnyAsync(c => c.EmpresaId == request.EmpresaId && c.CpfCnpj == request.CpfCnpj, cancellationToken))
                throw new RegraNegocioException(RegrasClienteCompartilhado.MensagemExclusivoNestaEmpresa(request.CpfCnpj));
        }
        else
        {
            // Exclusivo pode coexistir com um compartilhado do mesmo
            // documento (ex.: filial com outro e-mail): nesta empresa ele vence.
            var jaExisteClienteComMesmoDocumento = await _db.Clientes
                .AnyAsync(c => c.EmpresaId == request.EmpresaId && c.CpfCnpj == request.CpfCnpj, cancellationToken);

            if (jaExisteClienteComMesmoDocumento)
                throw new RegraNegocioException($"Já existe um cliente com o CPF/CNPJ '{request.CpfCnpj}' cadastrado para esta empresa.");
        }

        var uf = await ConferenciaMunicipio.UfConferidaAsync(_db, EnderecoCliente.Normalizar(request.CodigoMunicipio), request.Uf, cancellationToken);

        var apelido = request.Apelido.Trim();
        await ApelidoCliente.GarantirUnicoAsync(_db, empresaDoCliente, apelido, request.CpfCnpj, clienteIdIgnorado: null, cancellationToken);

        var cliente = new Cliente
        {
            EmpresaId = empresaDoCliente,
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
        if (request.RetencoesFederais is { } retencoes)
        {
            RegrasRetencoesFederais.ValidarPercentuais(retencoes);
            RetencoesCliente.Aplicar(cliente, retencoes);
        }

        _db.Clientes.Add(cliente);

        _auditLogWriter.Registrar("CadastrarCliente", "Cliente", cliente.Id, new { cliente.EmpresaId, cliente.CpfCnpj, cliente.Nome, cliente.Apelido, DisponivelPara = RegrasClienteCompartilhado.Texto(compartilhado) });

        await _db.SaveChangesAsync(cancellationToken);

        return cliente.Id;
    }
}
