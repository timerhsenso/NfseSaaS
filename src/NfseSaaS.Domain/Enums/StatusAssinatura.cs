namespace NfseSaaS.Domain.Enums;

/// <summary>Situação da assinatura de uma conta.</summary>
public enum StatusAssinatura
{
    /// <summary>Sem assinatura paga: teste grátis (que pode já ter acabado).</summary>
    Teste = 1,

    /// <summary>Assinatura com a última fatura paga.</summary>
    Ativa = 2,

    /// <summary>Fatura vencida e não paga (tolerância de Planos.ToleranciaAtrasoDias).</summary>
    Atrasada = 3,

    /// <summary>Assinatura cancelada (pelo cliente ou no Asaas).</summary>
    Cancelada = 4
}
