using NfseSaaS.Domain.Entities;
using Xunit;

namespace NfseSaaS.Tests;

public class TenantTests
{
    [Fact]
    public void Tenant_deve_possuir_Id_gerado_automaticamente()
    {
        var tenant = new Tenant { Cnpj = "04747304000178", RazaoSocial = "Empresa Teste" };

        Assert.NotEqual(Guid.Empty, tenant.Id);
    }

    [Fact]
    public void Tenant_deve_iniciar_ativo_por_padrao()
    {
        var tenant = new Tenant { Cnpj = "04747304000178", RazaoSocial = "Empresa Teste" };

        Assert.True(tenant.Ativo);
    }
}
