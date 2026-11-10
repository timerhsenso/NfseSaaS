using NfseSaaS.Application.UseCases.Municipios;
using NfseSaaS.Infrastructure.Persistence.Seeders;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>C9: busca de município sem acento e tabela embarcada do IBGE.</summary>
public class RegrasMunicipioTests
{
    [Theory]
    [InlineData("São Paulo", "sao paulo")]
    [InlineData("  Alta Floresta   D'Oeste ", "alta floresta d'oeste")]
    [InlineData("GOIÂNIA", "goiania")]
    [InlineData(null, "")]
    public void Busca_ignora_acento_maiuscula_e_espacos(string? texto, string esperado) =>
        Assert.Equal(esperado, RegrasMunicipio.NormalizarBusca(texto));

    [Theory]
    [InlineData("salvador ba", "salvador", "BA")]
    [InlineData("São Paulo/SP", "sao paulo", "SP")]
    [InlineData("rio de janeiro - rj", "rio de janeiro", "RJ")]
    [InlineData("salvador", "salvador", null)]
    [InlineData("pa", "pa", null)]
    public void UF_no_fim_da_busca_vira_filtro(string busca, string termo, string? uf)
    {
        var (t, u) = RegrasMunicipio.SepararUf(busca);
        Assert.Equal(termo, t);
        Assert.Equal(uf, u);
    }

    [Fact]
    public void Tabela_embarcada_tem_todos_os_municipios_com_codigo_e_uf_coerentes()
    {
        var tabela = MunicipioSeeder.CarregarTabela();
        Assert.True(tabela.Count >= 5570, $"Só {tabela.Count} municípios.");
        Assert.Equal(tabela.Count, tabela.Select(m => m.Codigo).Distinct().Count());
        Assert.All(tabela, m => Assert.Matches(@"^\d{7}$", m.Codigo));
        Assert.Contains(tabela, m => m.Codigo == "2927408" && m.Nome == "Salvador" && m.Uf == "BA");
        Assert.Contains(tabela, m => m.Codigo == "2919207" && m.Nome == "Lauro de Freitas" && m.Uf == "BA");
        Assert.Contains(tabela, m => m.Codigo == "5300108" && m.Uf == "DF");
        // Os 2 primeiros dígitos do código são a UF: um único prefixo por sigla.
        Assert.All(tabela.GroupBy(m => m.Uf), g => Assert.Single(g.Select(m => m.Codigo[..2]).Distinct()));
        Assert.Equal(27, tabela.Select(m => m.Uf).Distinct().Count());
    }
}
