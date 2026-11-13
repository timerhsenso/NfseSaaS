using NfseSaaS.Application.Abstractions;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>Nome do remetente no e-mail da nota: a empresa "via" a plataforma.</summary>
public class RegrasRemetenteEmailTests
{
    [Theory]
    [InlineData("LFMS", "LFMS CONSULTORIA LTDA", "LFMS via MicoEmpresa")]
    [InlineData(null, "LFMS CONSULTORIA LTDA", "LFMS CONSULTORIA LTDA via MicoEmpresa")]
    [InlineData("  ", "LFMS CONSULTORIA LTDA", "LFMS CONSULTORIA LTDA via MicoEmpresa")]
    [InlineData("Padaria \"Sol\" <ME>", null, "Padaria Sol ME via MicoEmpresa")]
    [InlineData("Linha\r\nquebrada", null, "Linhaquebrada via MicoEmpresa")]
    [InlineData(null, null, "MicoEmpresa")]
    [InlineData("\"<>\"", null, "MicoEmpresa")]
    public void Nome_da_empresa_via_plataforma(string? fantasia, string? razao, string esperado)
    {
        Assert.Equal(esperado, RegrasRemetenteEmail.NomeExibicao(fantasia, razao, "MicoEmpresa"));
    }

    [Fact]
    public void Nome_longo_e_cortado()
    {
        var nome = RegrasRemetenteEmail.NomeExibicao(new string('A', 80), null, "MicoEmpresa");
        Assert.Equal(new string('A', RegrasRemetenteEmail.TamanhoMaximoEmpresa) + " via MicoEmpresa", nome);
    }
}
