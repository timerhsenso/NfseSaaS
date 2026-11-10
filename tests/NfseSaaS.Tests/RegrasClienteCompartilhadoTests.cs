using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>C10: campo "Disponível para" — quando aparece, padrão por perfil e o que gravar.</summary>
public class RegrasClienteCompartilhadoTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(10, true)]
    public void Campo_so_aparece_com_mais_de_uma_empresa(int empresas, bool esperado) =>
        Assert.Equal(esperado, RegrasClienteCompartilhado.MostrarCampo(empresas));

    [Theory]
    [InlineData(PerfilConta.Empresa, 3, true)]
    [InlineData(PerfilConta.Contador, 3, false)]
    [InlineData(PerfilConta.Empresa, 1, false)]
    [InlineData(PerfilConta.Contador, 1, false)]
    public void Padrao_por_perfil(PerfilConta perfil, int empresas, bool esperado) =>
        Assert.Equal(esperado, RegrasClienteCompartilhado.PadraoCompartilhado(perfil, empresas));

    [Fact]
    public void Escolha_da_tela_vence_o_padrao()
    {
        Assert.False(RegrasClienteCompartilhado.Resolver(false, PerfilConta.Empresa, 3));
        Assert.True(RegrasClienteCompartilhado.Resolver(true, PerfilConta.Contador, 3));
        Assert.True(RegrasClienteCompartilhado.Resolver(null, PerfilConta.Empresa, 3));
        Assert.False(RegrasClienteCompartilhado.Resolver(null, PerfilConta.Contador, 3));
    }

    [Fact]
    public void Compartilhado_e_empresa_nula()
    {
        Assert.True(new Cliente { EmpresaId = null }.Compartilhado);
        Assert.False(new Cliente { EmpresaId = Guid.NewGuid() }.Compartilhado);
        Assert.True(new ClienteResponse(Guid.NewGuid(), null, "1", "n", "a", null, null, "", "", "", "", null, "", "", true, false, false,
            Array.Empty<string>(), false, DateTimeOffset.UtcNow, null).Compartilhado);
        Assert.Equal("Todas as empresas", RegrasClienteCompartilhado.Texto(true));
        Assert.Equal("Só esta empresa", RegrasClienteCompartilhado.Texto(false));
    }
}
