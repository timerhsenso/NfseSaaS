namespace NfseSaaS.Application.UseCases.Catalogos;

/// <summary>
/// Busca no catálogo oficial de Código de Tributação Nacional (cTribNac)
/// — tabela GLOBAL (não por tenant), alimentada por
/// CodigoTributacaoNacionalSeeder. Usado tanto pela busca do select2
/// (BuscarAsync) quanto pela validação de existência ao salvar um
/// Serviço (ExisteAsync).
/// </summary>
public interface IBuscarCodigoTributacaoNacionalUseCase
{
    /// <summary>
    /// Busca por código OU descrição (contém, sem diferenciar
    /// maiúsculas/minúsculas). <paramref name="limite"/> existe porque o
    /// catálogo, mesmo pequeno (338 códigos), não deve ser devolvido
    /// inteiro de uma vez pro select2 — só os resultados relevantes.
    /// </summary>
    Task<IReadOnlyList<CodigoTributacaoNacionalResponse>> BuscarAsync(string? termo, int limite, CancellationToken cancellationToken);

    /// <summary>Usado pela validação de Servico (existência real do código, não só formato).</summary>
    Task<bool> ExisteAsync(string codigo, CancellationToken cancellationToken);
}
