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
        // Casos de uso, validators e demais serviços de aplicação serão
        // registrados aqui conforme forem implementados na Fase 2.
        return services;
    }
}
