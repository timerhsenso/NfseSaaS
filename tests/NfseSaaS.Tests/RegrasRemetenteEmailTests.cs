using NfseSaaS.Application.Abstractions;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>Nome do remetente no e-mail da nota: o nome fantasia da empresa "via" a plataforma.</summary>
public class RegrasRemetenteEmailTests
{
    [Theory]
    [InlineData("RHSENSO", "LFMS - CONSULTORIA E DESENVOLVIMENTO DE SISTEMAS LTDA", "RHSENSO via MicoEmpresa")]
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

    [Theory]
    // Com separador: o que vem antes.
    [InlineData("RHSENSO - SISTEMAS E CONSULTORIA EM RECURSOS HUMANOS", "RHSENSO")]
    [InlineData("Clínica Vida | Medicina do Trabalho e Saúde Ocupacional", "Clínica Vida")]
    [InlineData("LFMS - CONSULTORIA E DESENVOLVIMENTO DE SISTEMAS LTDA", "LFMS")]
    // Sem separador: as palavras inteiras que cabem em 30.
    [InlineData("RHSENSO SISTEMAS E CONSULTORIA EM RECURSOS HUMANOS", "RHSENSO SISTEMAS E CONSULTORIA")]
    // Curto: inteiro, mesmo com hífen.
    [InlineData("Auto Peças - Centro", "Auto Peças - Centro")]
    public void Nome_longo_vira_a_primeira_porcao(string nome, string esperado)
    {
        Assert.Equal(esperado, RegrasRemetenteEmail.PrimeiraPorcao(nome));
        Assert.True(RegrasRemetenteEmail.PrimeiraPorcao(nome).Length <= RegrasRemetenteEmail.TamanhoMaximoEmpresa);
    }

    [Fact]
    public void Palavra_unica_enorme_e_cortada()
    {
        var nome = RegrasRemetenteEmail.NomeExibicao(new string('A', 80), null, "MicoEmpresa");
        Assert.Equal(new string('A', RegrasRemetenteEmail.TamanhoMaximoEmpresa) + " via MicoEmpresa", nome);
    }
}
