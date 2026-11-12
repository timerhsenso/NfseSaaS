using System.Text.Json;
using System.Text.Json.Serialization;
using NfseSaaS.Application.Exceptions;

namespace NfseSaaS.Application.UseCases.Aparencia;

/// <summary>Uma paleta de cores pronta. As cores ficam no CSS (html[data-paleta]); aqui só o que a tela mostra.</summary>
/// <param name="CorPrincipal">Cor dos botões e destaques (amostra no painel).</param>
/// <param name="CorMenu">Cor do menu lateral (amostra no painel).</param>
public sealed record PaletaCores(string Codigo, string Nome, string Descricao, string CorPrincipal, string CorMenu);

/// <summary>Uma opção de escolha (código + rótulo).</summary>
public sealed record OpcaoAparencia(string Codigo, string Nome);

/// <summary>
/// Catálogo da Aparência: paletas da conta (administrador) e opções de cada
/// usuário. Paletas fechadas — sem cor livre — para nenhuma combinação ficar
/// ilegível: todas passam no contraste mínimo (WCAG 4,5:1) no claro e no
/// escuro. Sóbrias de propósito (servem a contabilidade, RH, notas).
/// </summary>
public static class AparenciaCatalogo
{
    public const string PaletaPadrao = "mico";
    public const string FontePadrao = "m";
    public const string DensidadePadrao = "padrao";
    public const string ModoPadrao = "claro";

    public static readonly IReadOnlyList<PaletaCores> Paletas = new[]
    {
        new PaletaCores("mico", "Mico", "Azul do micoempresa (padrão)", "#2563eb", "#0b1220"),
        new PaletaCores("marinho", "Marinho", "Azul-marinho, sóbrio e institucional", "#1e3a8a", "#0a1630"),
        new PaletaCores("oceano", "Oceano", "Azul-petróleo, calmo e moderno", "#0e7490", "#082a33"),
        new PaletaCores("floresta", "Floresta", "Verde discreto", "#15803d", "#0b2416"),
        new PaletaCores("grafite", "Grafite", "Cinza neutro, sem cor de destaque", "#334155", "#111827"),
        new PaletaCores("vinho", "Vinho", "Bordô elegante", "#9f1239", "#2a0a14")
    };

    public static readonly IReadOnlyList<OpcaoAparencia> Fontes = new[]
    {
        new OpcaoAparencia("p", "Pequeno"), new OpcaoAparencia("m", "Médio"),
        new OpcaoAparencia("g", "Grande"), new OpcaoAparencia("gg", "Muito grande")
    };

    public static readonly IReadOnlyList<OpcaoAparencia> Densidades = new[]
    {
        new OpcaoAparencia("compacto", "Compacto"), new OpcaoAparencia("padrao", "Padrão"),
        new OpcaoAparencia("confortavel", "Confortável")
    };

    public static readonly IReadOnlyList<OpcaoAparencia> Modos = new[]
    {
        new OpcaoAparencia("claro", "Claro"), new OpcaoAparencia("escuro", "Escuro"),
        new OpcaoAparencia("auto", "Automático")
    };

    public static bool PaletaValida(string? codigo) => Paletas.Any(p => p.Codigo == codigo);

    /// <summary>Paleta gravada na conta, ou a padrão se vazia ou desconhecida (paleta removida do catálogo).</summary>
    public static string PaletaOuPadrao(string? codigo) => PaletaValida(codigo) ? codigo! : PaletaPadrao;
}

/// <summary>Preferências de aparência de um usuário (gravadas em JSON curto no usuário).</summary>
public sealed record PreferenciasAparencia(string Fonte, string Densidade, string Modo, bool MenuRecolhido)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static readonly PreferenciasAparencia Padrao =
        new(AparenciaCatalogo.FontePadrao, AparenciaCatalogo.DensidadePadrao, AparenciaCatalogo.ModoPadrao, false);

    /// <summary>
    /// Lê o JSON gravado. Tolerante: campo ausente ou desconhecido vira o
    /// padrão (opção removida do catálogo não quebra a tela de ninguém).
    /// </summary>
    public static PreferenciasAparencia Ler(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Padrao;
        try
        {
            var lido = JsonSerializer.Deserialize<PreferenciasAparencia>(json, Json);
            return lido is null ? Padrao : lido.Normalizada();
        }
        catch (JsonException)
        {
            return Padrao;
        }
    }

    public string Serializar() => JsonSerializer.Serialize(this, Json);

    private PreferenciasAparencia Normalizada() => new(
        AparenciaCatalogo.Fontes.Any(f => f.Codigo == Fonte) ? Fonte : AparenciaCatalogo.FontePadrao,
        AparenciaCatalogo.Densidades.Any(d => d.Codigo == Densidade) ? Densidade : AparenciaCatalogo.DensidadePadrao,
        AparenciaCatalogo.Modos.Any(m => m.Codigo == Modo) ? Modo : AparenciaCatalogo.ModoPadrao,
        MenuRecolhido);

    /// <summary>Valida o que veio da tela (aqui, ao contrário do Ler, opção inválida é erro).</summary>
    public static PreferenciasAparencia Validar(PreferenciasAparencia? recebidas)
    {
        if (recebidas is null)
            throw new RegraNegocioException("Informe as preferências.");
        if (!AparenciaCatalogo.Fontes.Any(f => f.Codigo == recebidas.Fonte))
            throw new RegraNegocioException("Tamanho do texto inválido.");
        if (!AparenciaCatalogo.Densidades.Any(d => d.Codigo == recebidas.Densidade))
            throw new RegraNegocioException("Tamanho dos botões e campos inválido.");
        if (!AparenciaCatalogo.Modos.Any(m => m.Codigo == recebidas.Modo))
            throw new RegraNegocioException("Modo de cor inválido.");
        return recebidas;
    }
}

/// <summary>O que o layout precisa para montar a página (atributos do &lt;html&gt;).</summary>
/// <param name="Salvo">O usuário já salvou preferências (senão o navegador migra o que tinha guardado).</param>
/// <param name="LogoVersao">Versão do logo da conta (vai na URL da imagem); nulo = sem logo.</param>
public sealed record AparenciaAtual(string Paleta, PreferenciasAparencia Preferencias, bool Salvo, bool PodeAlterarPaleta, string? LogoVersao = null);

/// <summary>Dados do painel "Aparência".</summary>
public sealed record AparenciaResponse(
    string Paleta,
    bool PodeAlterarPaleta,
    PreferenciasAparencia Preferencias,
    IReadOnlyList<PaletaCores> Paletas,
    IReadOnlyList<OpcaoAparencia> Fontes,
    IReadOnlyList<OpcaoAparencia> Densidades,
    IReadOnlyList<OpcaoAparencia> Modos,
    // B9: logo da conta. Disponível = o plano permite (Escritório); Versao nula = sem logo.
    bool LogoDisponivel = false,
    string? LogoVersao = null);

/// <summary>A imagem do logo, para servir no navegador.</summary>
public sealed record LogoContaArquivo(byte[] Conteudo, string TipoConteudo, string Versao);

public sealed record SalvarPaletaRequest(string Paleta);

/// <summary>Aparência do usuário logado: ler, salvar as preferências dele e (administrador) a paleta da conta.</summary>
public interface IAparenciaUseCase
{
    /// <summary>Para o layout (com cache): nunca falha — sem login ou com erro, devolve o padrão.</summary>
    Task<AparenciaAtual> AtualAsync(CancellationToken cancellationToken);

    Task<AparenciaResponse> ObterAsync(CancellationToken cancellationToken);

    Task<PreferenciasAparencia> SalvarPreferenciasAsync(PreferenciasAparencia preferencias, CancellationToken cancellationToken);

    /// <summary>Só administrador da conta (grupo administrador).</summary>
    Task SalvarPaletaAsync(SalvarPaletaRequest request, CancellationToken cancellationToken);

    /// <summary>B9 — só administrador, em conta com o plano Escritório. Devolve a versão nova.</summary>
    Task<string> SalvarLogoAsync(byte[] conteudo, CancellationToken cancellationToken);

    /// <summary>B9 — só administrador. Volta para a marca do micoempresa.</summary>
    Task RemoverLogoAsync(CancellationToken cancellationToken);

    /// <summary>Logo da conta do usuário logado (qualquer usuário da conta vê); null sem logo.</summary>
    Task<LogoContaArquivo?> ObterLogoAsync(CancellationToken cancellationToken);
}
