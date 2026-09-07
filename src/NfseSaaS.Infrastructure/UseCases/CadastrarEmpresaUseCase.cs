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

    public CadastrarEmpresaUseCase(AppDbContext db)
    {
        _db = db;
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
        await _db.SaveChangesAsync(cancellationToken);

        return empresa.Id;
    }
}
