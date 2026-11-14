using NfseSaaS.Application.Exceptions;

namespace NfseSaaS.Application.UseCases.Tour;

/// <summary>
/// TR1: tours guiados das telas (Driver.js, conteúdo em wwwroot/js/tour.js).
/// O servidor só guarda, por usuário, quais tours ele já viu — para o tour
/// abrir sozinho uma vez só, em qualquer computador ou celular.
/// </summary>
public interface ITourUseCase
{
    Task<IReadOnlyList<string>> VistosAsync(CancellationToken cancellationToken);

    Task MarcarVistoAsync(string? codigo, CancellationToken cancellationToken);

    /// <summary>"Rever os tours": esquece todos (abrem de novo em cada tela).</summary>
    Task ReverTodosAsync(CancellationToken cancellationToken);
}

/// <summary>TR1: catálogo dos tours e gravação compacta ("painel;notas").</summary>
public static class RegrasTour
{
    public const int TamanhoMaximo = 300;

    /// <summary>Um por tela com tour. Mesmo código em tour.js (TOURS).</summary>
    public static readonly IReadOnlySet<string> Codigos = new HashSet<string>(StringComparer.Ordinal)
    {
        "painel", "notas", "clientes", "contratos", "servicos", "empresas", "documentos", "modelos-email", "importar"
    };

    public static IReadOnlyList<string> Ler(string? gravado) =>
        (gravado ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Codigos.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Acrescenta o tour aos vistos (código conferido no catálogo).</summary>
    public static string Marcar(string? gravado, string? codigo)
    {
        var limpo = (codigo ?? string.Empty).Trim().ToLowerInvariant();
        if (!Codigos.Contains(limpo))
            throw new RegraNegocioException("Tour desconhecido.");
        var vistos = Ler(gravado).ToList();
        if (!vistos.Contains(limpo))
            vistos.Add(limpo);
        return string.Join(';', vistos);
    }
}
