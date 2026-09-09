using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Empresas;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ObterEmpresaPorIdUseCase : IObterEmpresaPorIdUseCase
{
    private readonly AppDbContext _db;

    public ObterEmpresaPorIdUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<EmpresaResponse> ExecutarAsync(Guid id, CancellationToken cancellationToken)
    {
        var empresa = await _db.Empresas.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (empresa is null)
            throw new RecursoNaoEncontradoException($"Empresa {id} não encontrada.");

        return new EmpresaResponse(
            empresa.Id, empresa.Cnpj, empresa.RazaoSocial, empresa.NomeFantasia, empresa.InscricaoMunicipal,
            empresa.CodigoMunicipio, empresa.Telefone, empresa.Email, empresa.Cep, empresa.Logradouro,
            empresa.Numero, empresa.Complemento, empresa.Bairro, empresa.Uf, empresa.OpSimpNac, empresa.RegApTribSN,
            empresa.RegEspTrib, empresa.TribIssqn, empresa.TpRetIssqn, empresa.CstPisCofins, empresa.TpRetPisCofins,
            empresa.PercentualTotalTributosSimplesNacional, empresa.Ativo, empresa.CreatedAt, empresa.UpdatedAt);
    }
}
