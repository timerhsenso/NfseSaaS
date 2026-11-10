namespace NfseSaaS.Domain.Enums;

/// <summary>Situação de um reajuste do preço de tabela para um assinante (B2).</summary>
public enum StatusReajusteAssinatura
{
    /// <summary>Avisado por e-mail; o novo valor entra em <c>AplicarEm</c>.</summary>
    Agendado = 1,

    /// <summary>Valor novo gravado no Asaas e na assinatura.</summary>
    Aplicado = 2,

    /// <summary>Não aplicado: cancelado pelo operador, ou a assinatura mudou (troca de plano, cancelamento, fundador).</summary>
    Cancelado = 3,

    /// <summary>O Asaas recusou várias vezes: o operador precisa olhar.</summary>
    Erro = 4
}
