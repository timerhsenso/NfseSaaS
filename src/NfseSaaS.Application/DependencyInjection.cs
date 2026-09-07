using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace NfseSaaS.Application;

/// <summary>
/// Ponto único de registro da camada Application no container de DI.
/// Mantém o Program.cs do Web enxuto (ver Requisito 16 da especificação).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Registra todo AbstractValidator<T> (FluentValidation) definido nesta
        // assembly como IValidator<T> no container — o ValidacaoAutomaticaFilter
        // (NfseSaaS.Web) resolve e executa automaticamente antes de cada action.
        //
        // Usa a assembly diretamente (não AddValidatorsFromAssemblyContaining<T>)
        // porque DependencyInjection é uma classe estática — tipo estático não
        // pode ser argumento de generic (CS0718).
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}
