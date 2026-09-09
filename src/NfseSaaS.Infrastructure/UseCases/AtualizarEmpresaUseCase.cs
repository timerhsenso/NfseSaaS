using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Empresas;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class AtualizarEmpresaUseCase : IAtualizarEmpresaUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public AtualizarEmpresaUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task ExecutarAsync(Guid id, AtualizarEmpresaRequest request, CancellationToken cancellationToken)
    {
        var empresa = await _db.Empresas.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (empresa is null)
            throw new RecursoNaoEncontradoException($"Empresa {id} não encontrada.");

        empresa.RazaoSocial = request.RazaoSocial;
        empresa.NomeFantasia = request.NomeFantasia;
        empresa.InscricaoMunicipal = request.InscricaoMunicipal;
        empresa.CodigoMunicipio = request.CodigoMunicipio;
        empresa.Telefone = request.Telefone;
        empresa.Email = request.Email;
        empresa.Cep = request.Cep;
        empresa.Logradouro = request.Logradouro;
        empresa.Numero = request.Numero;
        empresa.Complemento = request.Complemento;
        empresa.Bairro = request.Bairro;
        empresa.Uf = request.Uf;
        empresa.OpSimpNac = request.OpSimpNac;
        empresa.RegApTribSN = request.RegApTribSN;
        empresa.RegEspTrib = request.RegEspTrib;
        empresa.TribIssqn = request.TribIssqn;
        empresa.TpRetIssqn = request.TpRetIssqn;
        empresa.CstPisCofins = request.CstPisCofins;
        empresa.TpRetPisCofins = request.TpRetPisCofins;
        empresa.PercentualTotalTributosSimplesNacional = request.PercentualTotalTributosSimplesNacional;

        _auditLogWriter.Registrar("AtualizarEmpresa", "Empresa", empresa.Id, new { empresa.RazaoSocial });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
