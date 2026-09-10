using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ObterNfsePorIdUseCase : IObterNfsePorIdUseCase
{
    private readonly AppDbContext _db;

    public ObterNfsePorIdUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<NfseResponse> ExecutarAsync(Guid id, CancellationToken cancellationToken)
    {
        var nfse = await _db.NotasFiscais.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

        if (nfse is null)
            throw new RecursoNaoEncontradoException($"Nfse {id} não encontrada.");

        return new NfseResponse(
            nfse.Id, nfse.EmpresaId, nfse.ClienteId, nfse.ContratoId, nfse.NumeroDps, nfse.SerieDps, nfse.NumeroNfse, nfse.ChaveAcesso,
            nfse.DataCompetencia, nfse.DataEmissao, nfse.ValorServico, nfse.ValorLiquido, nfse.DescricaoServico, nfse.Status,
            nfse.TipoAmbiente, nfse.CodigoErro, nfse.MensagemErro, nfse.CreatedAt, nfse.UpdatedAt);
    }
}
