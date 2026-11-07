using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Cobranca;

namespace NfseSaaS.Web.Site;

/// <summary>Um plano como aparece no site público.</summary>
public sealed record PlanoPublico(
    string Nome,
    string Descricao,
    string Empresas,
    string Notas,
    decimal PrecoMensal,
    decimal PrecoFundador,
    bool Destaque);

/// <summary>Planos e campanha de fundador de um grupo (empresas ou escritórios).</summary>
public sealed record GrupoPrecosPublico(
    PublicoPlano Publico,
    IReadOnlyList<PlanoPublico> Planos,
    int VagasFundador,
    DateOnly? CampanhaAte)
{
    /// <summary>Ainda dá para garantir o preço de fundador (sobra vaga e a campanha não acabou).</summary>
    public bool FundadorAberto => VagasFundador > 0;
}

/// <summary>Tabela de preços do site: sempre a do catálogo (Plataforma › Planos).</summary>
public sealed record PrecosPublicosViewModel(
    GrupoPrecosPublico Empresas,
    GrupoPrecosPublico Escritorios,
    bool CobrancaAtiva,
    bool Completo)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Reais(decimal valor) =>
        valor == decimal.Truncate(valor) ? $"R$ {valor:0}" : valor.ToString("C", PtBr);

    public static string Data(DateOnly data) => data.ToString("dd/MM/yyyy", PtBr);
}

/// <summary>
/// Preços do site público ("/" e "/precos") lidos do catálogo de planos, com
/// as vagas de fundador e a data da campanha. Cache curto: a página inicial é
/// a mais acessada (inclusive por robôs) e o catálogo muda pouco.
/// </summary>
public sealed class PrecosViewComponent : ViewComponent
{
    private const string ChaveCache = "site:precos";
    private static readonly TimeSpan Validade = TimeSpan.FromMinutes(2);

    private readonly ICatalogoPlanos _catalogo;
    private readonly IMemoryCache _cache;
    private readonly CobrancaOptions _cobranca;

    public PrecosViewComponent(ICatalogoPlanos catalogo, IMemoryCache cache, IOptions<CobrancaOptions> cobranca)
    {
        _catalogo = catalogo;
        _cache = cache;
        _cobranca = cobranca.Value;
    }

    public async Task<IViewComponentResult> InvokeAsync(bool completo = false)
    {
        var (empresas, escritorios) = await _cache.GetOrCreateAsync(ChaveCache, async entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = Validade;
            return await MontarAsync(HttpContext.RequestAborted);
        });

        return View(new PrecosPublicosViewModel(empresas, escritorios, _cobranca.Ativa, completo));
    }

    private async Task<(GrupoPrecosPublico Empresas, GrupoPrecosPublico Escritorios)> MontarAsync(CancellationToken cancellationToken)
    {
        // O site mostra o que a tela Assinatura oferece: empresa vê os planos
        // de empresa; os de escritório são os oferecidos a contadores.
        var paraEmpresa = await _catalogo.PlanosAVendaAsync(PerfilConta.Empresa, cancellationToken);
        var paraContador = await _catalogo.PlanosAVendaAsync(PerfilConta.Contador, cancellationToken);

        return (
            await GrupoAsync(PublicoPlano.Empresas, paraEmpresa.Where(p => p.Publico == PublicoPlano.Empresas).ToList(), cancellationToken),
            await GrupoAsync(PublicoPlano.Escritorios, paraContador.Where(p => p.Publico == PublicoPlano.Escritorios).ToList(), cancellationToken));
    }

    private async Task<GrupoPrecosPublico> GrupoAsync(PublicoPlano publico, IReadOnlyList<PlanoVigente> planos, CancellationToken cancellationToken)
    {
        // Destaque: o segundo plano das empresas (carteira de contratos) e o
        // do meio dos escritórios, quando houver.
        var destaque = planos.Count > 1 ? planos[publico == PublicoPlano.Empresas ? 1 : planos.Count / 2].Codigo : (int?)null;
        var lista = planos.Select(p => new PlanoPublico(
            p.Nome,
            p.Descricao,
            p.MaxEmpresas == 1 ? "1 empresa" : publico == PublicoPlano.Escritorios ? $"até {p.MaxEmpresas} empresas ativas" : $"até {p.MaxEmpresas} empresas",
            p.NotasPorMes is { } n ? $"até {n} notas por mês" : "notas sem limite fixo",
            p.PrecoMensal,
            p.PrecoFundador,
            p.Codigo == destaque)).ToList();

        return new GrupoPrecosPublico(
            publico,
            lista,
            await _catalogo.VagasFundadorRestantesAsync(publico, cancellationToken),
            await _catalogo.CampanhaFundadorAteAsync(publico, cancellationToken));
    }
}
