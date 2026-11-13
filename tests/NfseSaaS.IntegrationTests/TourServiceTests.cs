using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>TR1: o "já visto" dos tours fica no próprio usuário (vale em qualquer aparelho).</summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class TourServiceTests
{
    private readonly AplicacaoTesteFactory _factory;

    public TourServiceTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class UsuarioFalso : ICurrentUser
    {
        public Guid? UserId { get; init; }
        public string? IpAddress => null;
    }

    [Fact]
    public async Task Marca_visto_por_usuario_e_rever_esquece()
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Tour " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        var usuario = new ApplicationUser { Id = Guid.NewGuid(), TenantId = conta.Id, UserName = $"tour-{s[..8]}@teste.com", Email = $"tour-{s[..8]}@teste.com" };
        var outro = new ApplicationUser { Id = Guid.NewGuid(), TenantId = conta.Id, UserName = $"outro-{s[..8]}@teste.com", Email = $"outro-{s[..8]}@teste.com" };
        db.Users.AddRange(usuario, outro);
        await db.SaveChangesAsync();

        try
        {
            var tour = new TourService(db, new UsuarioFalso { UserId = usuario.Id });
            Assert.Empty(await tour.VistosAsync(CancellationToken.None));

            await tour.MarcarVistoAsync("painel", CancellationToken.None);
            await tour.MarcarVistoAsync("notas", CancellationToken.None);
            await tour.MarcarVistoAsync("painel", CancellationToken.None);
            Assert.Equal(new[] { "painel", "notas" }, await tour.VistosAsync(CancellationToken.None));

            // Outro usuário da mesma conta: cada um tem o seu.
            Assert.Empty(await new TourService(db, new UsuarioFalso { UserId = outro.Id }).VistosAsync(CancellationToken.None));
            // Sem login: nada (e não quebra).
            Assert.Empty(await new TourService(db, new UsuarioFalso()).VistosAsync(CancellationToken.None));

            await tour.ReverTodosAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            Assert.Empty(await tour.VistosAsync(CancellationToken.None));
            Assert.Null(await db.Users.AsNoTracking().Where(u => u.Id == usuario.Id).Select(u => u.ToursVistos).SingleAsync());
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.Users.Where(u => u.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }
}
