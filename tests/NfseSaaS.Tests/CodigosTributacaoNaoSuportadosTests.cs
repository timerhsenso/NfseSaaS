using System.Text.Json;
using System.Text.RegularExpressions;
using NfseSaaS.Application.UseCases.Catalogos;
using NfseSaaS.Application.UseCases.Servicos;
using Xunit;

namespace NfseSaaS.Tests;

public class CodigosTributacaoNaoSuportadosTests
{
    [Theory]
    [InlineData("070201")]
    [InlineData("07.02.02")]
    [InlineData("070501")]
    [InlineData("141404")]
    public void Codigos_de_obra_sao_bloqueados_com_explicacao(string codigo)
    {
        var motivo = CodigosTributacaoNaoSuportados.Motivo(codigo);
        Assert.NotNull(motivo);
        Assert.Contains("obra", motivo);
        Assert.Contains("E0370", motivo);
        Assert.Contains("Emissor Nacional", motivo);
    }

    [Theory]
    [InlineData("120101")]
    [InlineData("120801")]
    [InlineData("121701")]
    public void Item_12_eventos_e_bloqueado(string codigo)
    {
        Assert.Contains("E0390", CodigosTributacaoNaoSuportados.Motivo(codigo));
        Assert.Equal("não disponível: exige dados do evento", CodigosTributacaoNaoSuportados.Aviso(codigo));
    }

    [Theory]
    [InlineData("010101")] // desenvolvimento de sistemas
    [InlineData("070301")] // projetos de engenharia (não exige obra)
    [InlineData("171001")] // organização de feiras é item 17, não 12
    [InlineData("030301")] // salão de eventos é item 3
    [InlineData("")]
    [InlineData(null)]
    public void Demais_codigos_seguem_livres(string? codigo)
    {
        Assert.Null(CodigosTributacaoNaoSuportados.Motivo(codigo));
        Assert.Null(CodigosTributacaoNaoSuportados.Aviso(codigo));
    }

    /// <summary>A lista do código é a mesma da mensagem oficial E0370 (Anexo I) — e todos existem no catálogo cTribNac.</summary>
    [Fact]
    public void Lista_de_obra_bate_com_a_regra_oficial_E0370_e_com_o_catalogo()
    {
        var rejeicoes = JsonDocument.Parse(File.ReadAllText(Achar("src/NfseSaaS.Application/Rejeicoes/rejeicoes-sefin.json")));
        var e0370 = Itens(rejeicoes.RootElement).Single(e => e.GetProperty("codigo").GetString() == "E0370").GetProperty("mensagem").GetString()!;

        // A mensagem oficial tem erros de digitação ("07.05,01", "1414.03"): só os dígitos importam.
        var oficiais = Regex.Matches(e0370, @"\d{2}\.?\d{2}[.,]\d{2}")
            .Select(m => new string(m.Value.Where(char.IsDigit).ToArray()))
            .ToHashSet();
        Assert.Equal(oficiais.OrderBy(c => c), CodigosTributacaoNaoSuportados.ExigemObra.OrderBy(c => c));

        var catalogo = JsonDocument.Parse(File.ReadAllText(Achar("src/NfseSaaS.Infrastructure/Persistence/Seeders/Dados/ctribnac.json")))
            .RootElement.EnumerateArray().Select(c => c.GetProperty("codigo").GetString()).ToHashSet();
        Assert.All(CodigosTributacaoNaoSuportados.ExigemObra, c => Assert.Contains(c, catalogo));
        Assert.True(catalogo.Count(c => c!.StartsWith("12")) > 10);
    }

    [Fact]
    public async Task Cadastro_do_servico_recusa_codigo_de_obra()
    {
        var validador = new CadastrarServicoRequestValidator(new CatalogoQueAceitaTudo(), new NbsQueAceitaTudo());
        var resultado = await validador.ValidateAsync(new CadastrarServicoRequest(Guid.NewGuid(), "Reforma", "070501", "101010100", 100m));

        Assert.False(resultado.IsValid);
        Assert.Contains(resultado.Errors, e => e.ErrorMessage.Contains("E0370"));

        var livre = await validador.ValidateAsync(new CadastrarServicoRequest(Guid.NewGuid(), "Sistemas", "010101", "101010100", 100m));
        Assert.True(livre.IsValid);
    }

    [Fact]
    public void Lista_de_servicos_mostra_o_aviso_de_quem_ja_tinha_o_codigo()
    {
        var antigo = new ServicoResponse(Guid.NewGuid(), Guid.NewGuid(), "Show", "120701", "1", 1m, true, DateTimeOffset.UtcNow, null);
        Assert.Contains("E0390", antigo.AvisoCodigo);
        Assert.Null((antigo with { CodigoTributacaoNacional = "010101" }).AvisoCodigo);
    }

    private sealed class CatalogoQueAceitaTudo : IBuscarCodigoTributacaoNacionalUseCase
    {
        public Task<IReadOnlyList<CodigoTributacaoNacionalResponse>> BuscarAsync(string? termo, int limite, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CodigoTributacaoNacionalResponse>>(Array.Empty<CodigoTributacaoNacionalResponse>());
        public Task<bool> ExisteAsync(string codigo, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class NbsQueAceitaTudo : IBuscarCodigoNbsUseCase
    {
        public Task<IReadOnlyList<CodigoNbsResponse>> BuscarAsync(string? termo, int limite, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CodigoNbsResponse>>(Array.Empty<CodigoNbsResponse>());
        public Task<bool> ExisteAsync(string codigo, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private static IEnumerable<JsonElement> Itens(JsonElement raiz) =>
        raiz.ValueKind == JsonValueKind.Array ? raiz.EnumerateArray() : raiz.GetProperty("rejeicoes").EnumerateArray();

    private static string Achar(string relativo)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var caminho = Path.Combine(dir.FullName, relativo);
            if (File.Exists(caminho))
                return caminho;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relativo);
    }
}
