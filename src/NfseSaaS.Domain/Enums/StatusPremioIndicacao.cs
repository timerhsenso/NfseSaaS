namespace NfseSaaS.Domain.Enums;

/// <summary>Situação de um prêmio do "Indique e ganhe" (1 mês grátis).</summary>
public enum StatusPremioIndicacao
{
    /// <summary>Garantido, esperando a conta ter assinatura em dia (ou a próxima tentativa).</summary>
    Pendente = 1,

    /// <summary>
    /// Chamada ao Asaas em andamento. Gravado ANTES de chamar: se o processo
    /// cair no meio, o prêmio não é aplicado de novo sozinho (vira Erro para
    /// o operador conferir), o que evita dispensar duas faturas.
    /// </summary>
    Aplicando = 2,

    /// <summary>Mês grátis concedido (fatura dispensada ou vencimento adiado).</summary>
    Aplicado = 3,

    /// <summary>Falhou e precisa do operador (Plataforma › Contas › Tentar de novo).</summary>
    Erro = 4
}

/// <summary>Quem recebe o prêmio, em relação à conta indicada que pagou.</summary>
public enum PapelPremioIndicacao
{
    /// <summary>A própria conta indicada (pagou a primeira fatura).</summary>
    ContaIndicada = 1,

    /// <summary>A conta dona do link de indicação.</summary>
    QuemIndicou = 2
}
