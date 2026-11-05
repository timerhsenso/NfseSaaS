namespace NfseSaaS.Domain.Cobranca;

/// <summary>Planos do micoempresa. Código estável (gravado na Assinatura).</summary>
public enum PlanoCodigo
{
    Mico = 1,
    Empresa = 2
}

/// <summary>
/// Um plano: limites e preços. Os limites são "suaves": servem para avisar e
/// orientar a troca de plano, nunca para travar uma emissão no meio do mês.
/// </summary>
public sealed record PlanoAssinatura(
    PlanoCodigo Codigo,
    string Nome,
    string Descricao,
    int MaxEmpresas,
    int NotasPorMes,
    decimal PrecoMensal,
    decimal PrecoFundador);

/// <summary>
/// Catálogo de planos e regras comerciais (ver claude/plano-comercial.md no
/// projeto). Ficam no código, não no banco: mudam raramente e cada mudança é
/// uma decisão de negócio que merece revisão e CHANGELOG.
/// </summary>
public static class Planos
{
    public static readonly PlanoAssinatura Mico = new(
        PlanoCodigo.Mico, "Mico", "Para quem emite poucas notas", 1, 15, 29m, 19m);

    public static readonly PlanoAssinatura Empresa = new(
        PlanoCodigo.Empresa, "Empresa", "Para carteira de contratos", 3, 100, 59m, 39m);

    public static readonly IReadOnlyList<PlanoAssinatura> Todos = new[] { Mico, Empresa };

    /// <summary>Os N primeiros assinantes pagam o preço de fundador, mantido enquanto a assinatura existir.</summary>
    public const int VagasFundador = 20;

    /// <summary>Teste grátis em produção: o que acabar primeiro (homologação é sempre livre).</summary>
    public const int TesteDias = 30;
    public const int TesteNotas = 10;

    /// <summary>Dias depois do vencimento em que a emissão em produção ainda funciona.</summary>
    public const int ToleranciaAtrasoDias = 7;

    public static PlanoAssinatura Obter(PlanoCodigo codigo) =>
        Todos.FirstOrDefault(p => p.Codigo == codigo)
        ?? throw new ArgumentOutOfRangeException(nameof(codigo), codigo, "Plano desconhecido.");
}
