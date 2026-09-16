namespace NfseSaaS.Application.UseCases.Catalogos;

/// <summary>
/// Busca no catálogo oficial de NBS — tabela GLOBAL (não por tenant),
/// alimentada por CodigoNbsSeeder. Mesmo raciocínio de
/// IBuscarCodigoTributacaoNacionalUseCase.
/// </summary>
public interface IBuscarCodigoNbsUseCase
{
    Task<IReadOnlyList<CodigoNbsResponse>> BuscarAsync(string? termo, int limite, CancellationToken cancellationToken);

    Task<bool> ExisteAsync(string codigo, CancellationToken cancellationToken);
}
