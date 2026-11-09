namespace NfseSaaS.Domain.Enums;

/// <summary>Quem encerrou a conta (D2).</summary>
public enum OrigemEncerramento
{
    /// <summary>Administrador da conta, pela tela "Meus dados", com código por e-mail.</summary>
    Cliente = 1,

    /// <summary>Operador da plataforma (Plataforma › Contas).</summary>
    Plataforma = 2,

    /// <summary>Job: assinatura cancelada há mais do que o prazo configurado (desligado por padrão).</summary>
    Automatico = 3
}
