using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Consultas;
using NfseSaaS.Application.UseCases.Municipios;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Municípios da tabela local (C9). Também responde a consulta do nome pelo
/// código (api/consultas/municipio/{codigo}), que antes ia à API do IBGE.
/// </summary>
public sealed class MunicipiosService : IMunicipiosUseCase, IConsultarMunicipioUseCase
{
    private readonly AppDbContext _db;

    public MunicipiosService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<MunicipioResponse>> BuscarAsync(string? busca, string? uf, int limite, CancellationToken cancellationToken)
    {
        var digitos = new string((busca ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digitos.Length == 7)
            return await ObterAsync(digitos, cancellationToken) is { } porCodigo ? [porCodigo] : [];

        var (termo, ufNaBusca) = RegrasMunicipio.SepararUf(busca);
        var ufFiltro = (string.IsNullOrWhiteSpace(uf) ? ufNaBusca : uf.Trim().ToUpperInvariant());
        if (termo.Length < 2)
            return [];

        var consulta = _db.Municipios.AsNoTracking().Where(m => m.NomeBusca.Contains(termo));
        if (!string.IsNullOrEmpty(ufFiltro))
            consulta = consulta.Where(m => m.Uf == ufFiltro);

        // Quem começa com o termo vem primeiro ("salva" → Salvador antes de Bom Jesus da Salvação).
        return await consulta
            .OrderBy(m => m.NomeBusca.StartsWith(termo) ? 0 : 1)
            .ThenBy(m => m.NomeBusca)
            .ThenBy(m => m.Uf)
            .Take(Math.Clamp(limite, 1, 50))
            .Select(m => new MunicipioResponse(m.Codigo, m.Nome, m.Uf))
            .ToListAsync(cancellationToken);
    }

    public Task<MunicipioResponse?> ObterAsync(string codigo, CancellationToken cancellationToken)
    {
        var limpo = new string((codigo ?? string.Empty).Where(char.IsDigit).ToArray());
        return _db.Municipios.AsNoTracking()
            .Where(m => m.Codigo == limpo)
            .Select(m => new MunicipioResponse(m.Codigo, m.Nome, m.Uf))
            .FirstOrDefaultAsync(cancellationToken);
    }

    async Task<ConsultaMunicipioResponse> IConsultarMunicipioUseCase.ExecutarAsync(string codigoMunicipio, CancellationToken cancellationToken)
    {
        var codigo = new string((codigoMunicipio ?? string.Empty).Where(char.IsDigit).ToArray());
        if (codigo.Length != 7)
            throw new RegraNegocioException("O código IBGE do município tem 7 dígitos.");

        var municipio = await ObterAsync(codigo, cancellationToken)
            ?? throw new RecursoNaoEncontradoException(RegrasMunicipio.MensagemNaoExiste(codigo));
        return new ConsultaMunicipioResponse(municipio.Codigo, municipio.Nome, municipio.Uf);
    }
}
