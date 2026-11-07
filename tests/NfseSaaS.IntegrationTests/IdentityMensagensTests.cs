using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>O UserManager da aplicação devolve as mensagens em português (IdentityErrorDescriberPtBr registrado).</summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class IdentityMensagensTests
{
    private readonly AplicacaoTesteFactory _factory;

    public IdentityMensagensTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Senha_fraca_e_email_repetido_vem_em_portugues()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var usuarios = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Identity " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        db.TenantIdOverrideDeSistema = conta.Id;

        try
        {
            var email = $"id{s}@teste.com";
            var fraca = await usuarios.CreateAsync(new ApplicationUser { TenantId = conta.Id, UserName = email, Email = email }, "abc");
            Assert.False(fraca.Succeeded);
            Assert.Contains(fraca.Errors, e => e.Description == "A senha precisa ter pelo menos 8 caracteres.");
            Assert.Contains(fraca.Errors, e => e.Description.Contains("maiúscula"));

            Assert.True((await usuarios.CreateAsync(new ApplicationUser { TenantId = conta.Id, UserName = email, Email = email }, "Senha@forte123")).Succeeded);
            var repetido = await usuarios.CreateAsync(new ApplicationUser { TenantId = conta.Id, UserName = email, Email = email }, "Senha@forte123");
            Assert.Contains(repetido.Errors, e => e.Description == $"O e-mail \"{email}\" já está em uso.");
        }
        finally
        {
            db.TenantIdOverrideDeSistema = null;
            db.ChangeTracker.Clear();
            await db.Users.Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }
}
