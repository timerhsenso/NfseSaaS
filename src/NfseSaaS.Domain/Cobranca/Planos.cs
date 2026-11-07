namespace NfseSaaS.Domain.Cobranca;

/// <summary>
/// Regras comerciais FIXAS (ver claude/plano-comercial.md no projeto). Os
/// planos, preços e vagas de fundador ficam no banco (tabelas planos,
/// precos_planos e vagas_fundador), administrados em Plataforma › Planos.
/// Os códigos abaixo são os dos planos que já nascem cadastrados.
/// </summary>
public static class Planos
{
    public const int CodigoMico = 1;
    public const int CodigoEmpresa = 2;
    public const int CodigoEscritorio10 = 3;
    public const int CodigoEscritorio25 = 4;
    public const int CodigoEscritorio50 = 5;

    /// <summary>Teste grátis em produção: o que acabar primeiro (homologação é sempre livre).</summary>
    public const int TesteDias = 30;
    public const int TesteNotas = 10;

    /// <summary>
    /// Dias depois do vencimento em que a emissão em produção ainda funciona
    /// (bloqueia no dia seguinte). Os Termos de uso (item 8) falam em 10 dias:
    /// mudar aqui exige mudar lá e subir a versão dos termos.
    /// </summary>
    public const int ToleranciaAtrasoDias = 10;
}

/// <summary>
/// Grupo do plano: decide de qual "pote" de vagas de fundador ele tira
/// (empresas e escritórios têm vagas separadas).
/// </summary>
public enum PublicoPlano
{
    Empresas = 1,
    Escritorios = 2
}
