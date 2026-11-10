using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Fiscal.IbsCbs;
using NfseSaaS.Application.UseCases.Catalogos;
using NfseSaaS.Application.UseCases.Municipios;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// Catálogos oficiais globais (não por tenant/Empresa) usados como
/// referência em formulários — hoje só cTribNac. Só leitura: estes
/// catálogos são alimentados por seed (ver CodigoTributacaoNacionalSeeder),
/// nunca por CRUD de usuário. Aberto a qualquer usuário autenticado,
/// mesmo raciocínio do ConsultasController.
/// </summary>
[ApiController]
[Authorize]
[Route("api/catalogos")]
public sealed class CatalogosController : ControllerBase
{
    private readonly IBuscarCodigoTributacaoNacionalUseCase _buscarCodigoTributacaoNacional;
    private readonly IBuscarCodigoNbsUseCase _buscarCodigoNbs;
    private readonly IMunicipiosUseCase _municipios;

    public CatalogosController(
        IBuscarCodigoTributacaoNacionalUseCase buscarCodigoTributacaoNacional,
        IBuscarCodigoNbsUseCase buscarCodigoNbs,
        IMunicipiosUseCase municipios)
    {
        _buscarCodigoTributacaoNacional = buscarCodigoTributacaoNacional;
        _buscarCodigoNbs = buscarCodigoNbs;
        _municipios = municipios;
    }

    /// <summary>
    /// Municípios do IBGE pelo nome (C9), no formato do select2. Aceita
    /// "salvador", "salvador ba", "são paulo/sp" ou o código de 7 dígitos;
    /// uf filtra o estado.
    /// </summary>
    [HttpGet("municipios")]
    public async Task<IActionResult> BuscarMunicipios([FromQuery] string? busca, [FromQuery] string? uf, [FromQuery] int limite, CancellationToken cancellationToken)
    {
        var limiteReal = limite is > 0 and <= 50 ? limite : RegrasMunicipio.LimiteBusca;
        var resultado = await _municipios.BuscarAsync(busca, uf, limiteReal, cancellationToken);
        return Ok(new
        {
            results = resultado.Select(m => new { id = m.Codigo, text = RegrasMunicipio.Exibicao(m.Nome, m.Uf), nome = m.Nome, uf = m.Uf })
        });
    }

    /// <summary>
    /// Formato de resposta já pensado pro select2 (AJAX data source):
    /// { results: [{ id, text }] }. limite tem teto de 50 — é uma busca
    /// de autocomplete, não uma listagem paginada.
    /// </summary>
    [HttpGet("ctribnac")]
    public async Task<IActionResult> BuscarCTribNac([FromQuery] string? busca, [FromQuery] int limite, CancellationToken cancellationToken)
    {
        var limiteReal = limite is > 0 and <= 50 ? limite : 20;
        var resultado = await _buscarCodigoTributacaoNacional.BuscarAsync(busca, limiteReal, cancellationToken);

        return Ok(new
        {
            // Códigos de obra/evento aparecem, mas desabilitados e com o
            // motivo (CodigosTributacaoNaoSuportados): a pessoa entende por
            // que não pode usar, em vez de achar que o código "sumiu".
            results = resultado.Select(c => CodigosTributacaoNaoSuportados.Aviso(c.Codigo) is { } aviso
                ? (object)new { id = c.Codigo, text = $"{FormatarCodigo(c.Codigo)} — {c.Descricao} ({aviso})", disabled = true }
                : new { id = c.Codigo, text = $"{FormatarCodigo(c.Codigo)} — {c.Descricao}" })
        });
    }

    /// <summary>Mesmo formato de BuscarCTribNac, pro select2 do campo Código NBS.</summary>
    [HttpGet("nbs")]
    public async Task<IActionResult> BuscarNbs([FromQuery] string? busca, [FromQuery] int limite, CancellationToken cancellationToken)
    {
        var limiteReal = limite is > 0 and <= 50 ? limite : 20;
        var resultado = await _buscarCodigoNbs.BuscarAsync(busca, limiteReal, cancellationToken);

        return Ok(new
        {
            results = resultado.Select(c => new { id = c.Codigo, text = $"{FormatarCodigoNbs(c.Codigo)} — {c.Descricao}" })
        });
    }

    /// <summary>
    /// IBS/CBS (F1): opções da tabela oficial (Anexo VIII) para o item do
    /// cTribNac, já com a sugestão que vale quando o Serviço deixa os campos
    /// em branco (RegrasIbsCbs.Resolver). Indicadores que o sistema ainda
    /// não consegue emitir vêm marcados (suportado = false).
    /// </summary>
    [HttpGet("ibscbs")]
    public IActionResult OpcoesIbsCbs([FromQuery] string? codigoTributacaoNacional)
    {
        var opcoes = CatalogoIbsCbs.OpcoesDoServico(codigoTributacaoNacional);
        var sugestao = string.IsNullOrWhiteSpace(codigoTributacaoNacional)
            ? null
            : RegrasIbsCbs.Resolver(codigoTributacaoNacional, null, null);

        return Ok(new
        {
            versaoAnexoVII = CatalogoIbsCbs.VersaoAnexoVII,
            versaoAnexoVIII = CatalogoIbsCbs.VersaoAnexoVIII,
            item = opcoes?.Item,
            descricaoItem = opcoes?.Descricao,
            indicadores = (opcoes?.IndicadoresOperacao ?? []).Select(i => new { codigo = i.Codigo, rotulo = i.Rotulo, suportado = i.SuportadoPeloSistema }),
            classificacoes = (opcoes?.Classificacoes ?? []).Select(c => new { codigo = c.Codigo, rotulo = c.Rotulo, cst = c.Cst, comTributacao = RegrasIbsCbs.TemTributacao(c.Cst) }),
            // Todas as classificações conhecidas, para quem precisar de outra (o item pode admitir mais que a tabela inicial mostra).
            todasClassificacoes = CatalogoIbsCbs.Classificacoes.OrderBy(c => c.Codigo)
                .Select(c => new { codigo = c.Codigo, rotulo = c.Rotulo, cst = c.Cst, comTributacao = RegrasIbsCbs.TemTributacao(c.Cst) }),
            sugestao = sugestao?.Classificacao is { } s ? new { cIndOp = s.CIndOp, cClassTrib = s.CClassTrib } : null,
            problemaSugestao = sugestao?.Problema
        });
    }

    /// <summary>"010601" -> "01.06.01", só pra exibição — o valor salvo continua sem pontuação.</summary>
    private static string FormatarCodigo(string codigo) =>
        codigo.Length == 6 ? $"{codigo[..2]}.{codigo[2..4]}.{codigo[4..]}" : codigo;

    /// <summary>"101011100" -> "1.0101.11.00", formato oficial do NBS (item.subitem.posição.subposição).</summary>
    private static string FormatarCodigoNbs(string codigo) =>
        codigo.Length == 9 ? $"{codigo[..1]}.{codigo[1..5]}.{codigo[5..7]}.{codigo[7..]}" : codigo;
}
