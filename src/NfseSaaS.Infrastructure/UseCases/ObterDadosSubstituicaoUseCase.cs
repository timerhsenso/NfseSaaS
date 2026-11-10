using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>C7: motivos e campos travados da substituição (a emissão confere tudo de novo).</summary>
public sealed class ObterDadosSubstituicaoUseCase : IObterDadosSubstituicaoUseCase
{
    private readonly AppDbContext _db;

    public ObterDadosSubstituicaoUseCase(AppDbContext db)
    {
        _db = db;
    }

    public async Task<DadosSubstituicaoResponse> ExecutarAsync(Guid nfseId, CancellationToken cancellationToken)
    {
        var dados = await (
            from n in _db.NotasFiscais.AsNoTracking()
            join e in _db.Empresas.AsNoTracking() on n.EmpresaId equals e.Id
            where n.Id == nfseId
            select new { n.Status, n.ChaveAcesso, n.TipoAmbiente, AmbienteEmpresa = e.TipoAmbiente, e.OpSimpNac })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Nfse {nfseId} não encontrada.");

        var substituta = await _db.NotasFiscais.AsNoTracking()
            .Where(s => s.SubstituiNfseId == nfseId && (s.Status == NfseStatus.Processando || s.Status == NfseStatus.Autorizada))
            .Select(s => (int?)s.NumeroDps)
            .FirstOrDefaultAsync(cancellationToken);

        string? impedimento =
            dados.Status != NfseStatus.Autorizada || string.IsNullOrWhiteSpace(dados.ChaveAcesso)
                ? "Só uma nota autorizada pode ser substituída."
            : substituta is not null
                ? $"Esta nota já tem uma substituta (DPS nº {substituta})."
            : dados.TipoAmbiente != dados.AmbienteEmpresa
                ? "A nota é de outro ambiente (homologação/produção) que o atual da empresa."
            : null;

        var travados = RegrasSubstituicao.CamposTravados(dados.OpSimpNac);
        return new DadosSubstituicaoResponse(
            impedimento,
            RegrasSubstituicao.Motivos,
            travados,
            string.Join(", ", travados.Select(RegrasSubstituicao.Nome)));
    }
}
