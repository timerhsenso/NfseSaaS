using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.Empresas;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Implementação real do caso de uso de cadastro de Empresa. Vive em
/// Infrastructure (não em Application) porque toca EF Core diretamente —
/// sem repository genérico nem UnitOfWork artificial em cima do EF Core,
/// como pedido na especificação original.
/// </summary>
public sealed class CadastrarEmpresaUseCase : ICadastrarEmpresaUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;

    public CadastrarEmpresaUseCase(AppDbContext db, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<Guid> ExecutarAsync(CadastrarEmpresaRequest request, CancellationToken cancellationToken)
    {
        var empresa = new Empresa
        {
            Cnpj = request.Cnpj,
            RazaoSocial = request.RazaoSocial,
            NomeFantasia = request.NomeFantasia,
            InscricaoMunicipal = request.InscricaoMunicipal,
            CodigoMunicipio = request.CodigoMunicipio,
            Telefone = request.Telefone,
            Email = request.Email,
            Cep = request.Cep,
            Logradouro = request.Logradouro,
            Numero = request.Numero,
            Complemento = request.Complemento,
            Bairro = request.Bairro,
            Uf = request.Uf,
            OpSimpNac = request.OpSimpNac,
            RegApTribSN = request.RegApTribSN,
            RegEspTrib = request.RegEspTrib,
            TribIssqn = request.TribIssqn,
            TpRetIssqn = request.TpRetIssqn,
            CstPisCofins = request.CstPisCofins,
            TpRetPisCofins = request.TpRetPisCofins,
            PercentualTotalTributosSimplesNacional = request.PercentualTotalTributosSimplesNacional
        };

        // TenantId é preenchido automaticamente pelo AppDbContext.SaveChanges
        // (ver ApplyTenantIsolation) — nunca setado manualmente aqui.
        _db.Empresas.Add(empresa);

        _auditLogWriter.Registrar("CadastrarEmpresa", "Empresa", empresa.Id, new { empresa.Cnpj, empresa.RazaoSocial });

        await _db.SaveChangesAsync(cancellationToken);

        return empresa.Id;
    }
}
