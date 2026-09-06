using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.MultiTenancy;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure;

/// <summary>
/// Ponto único de registro da camada Infrastructure no container de DI.
/// Mantém o Program.cs do Web enxuto (ver Requisito 16 da especificação).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection não configurada. " +
                "Em desenvolvimento, defina via 'dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" \"...\"'.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentTenant, CurrentTenant>();

        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                // Regras mínimas de senha para esta fase. Serão revisadas
                // conforme requisitos de segurança do produto evoluírem.
                options.Password.RequiredLength = 8;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        return services;
    }
}
