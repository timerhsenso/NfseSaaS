using System.Text.RegularExpressions;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>
/// Tabelas em português sem depender da CDN: a tradução é local, padrão
/// global, carregada antes das telas, e nenhuma tela volta a buscar o
/// arquivo de tradução na internet.
/// </summary>
public class TraducaoDataTablesTests
{
    private static readonly string RaizWeb = AcharRaizWeb();

    [Fact]
    public void Nenhuma_tela_busca_traducao_na_cdn()
    {
        var telas = Directory.GetFiles(Path.Combine(RaizWeb, "wwwroot", "js"), "*.js", SearchOption.AllDirectories);
        Assert.NotEmpty(telas);
        foreach (var arquivo in telas)
            Assert.DoesNotContain("cdn.datatables.net/plug-ins", File.ReadAllText(arquivo));
    }

    [Fact]
    public void Traducao_local_tem_os_textos_principais()
    {
        var js = File.ReadAllText(Path.Combine(RaizWeb, "wwwroot", "js", "datatables-pt-br.js"));
        Assert.Contains("DataTable.defaults.language", js);
        Assert.Contains("\"emptyTable\": \"Nenhum registro encontrado\"", js);
        Assert.Contains("\"search\": \"Pesquisar\"", js);
        Assert.Contains("\"next\": \"Próximo\"", js);
        Assert.Contains("_TOTAL_", js);
    }

    [Fact]
    public void Layout_carrega_traducao_depois_do_datatables_e_antes_das_telas()
    {
        var layout = File.ReadAllText(Path.Combine(RaizWeb, "Views", "Shared", "_Layout.cshtml"));
        var datatables = layout.IndexOf("dataTables.min.js", StringComparison.Ordinal);
        var traducao = layout.IndexOf("datatables-pt-br.js", StringComparison.Ordinal);
        var site = layout.IndexOf("~/js/site.js", StringComparison.Ordinal);
        var telas = layout.IndexOf("RenderSectionAsync(\"Scripts\"", StringComparison.Ordinal);

        Assert.True(datatables >= 0 && traducao > datatables, "tradução antes do DataTables");
        Assert.True(traducao < site, "tradução depois do site.js");
        Assert.True(telas > traducao, "tradução depois das telas");
        Assert.Single(Regex.Matches(layout, "datatables-pt-br\\.js"));
    }

    private static string AcharRaizWeb()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var caminho = Path.Combine(dir.FullName, "src", "NfseSaaS.Web");
            if (Directory.Exists(Path.Combine(caminho, "wwwroot")))
                return caminho;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("src/NfseSaaS.Web não encontrado a partir de " + AppContext.BaseDirectory);
    }
}
