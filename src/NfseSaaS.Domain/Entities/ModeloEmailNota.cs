using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Modelo do e-mail que leva a nota (PDF e XML) ao cliente — cadastro
/// "Modelos de e-mail". Reutilizável: a conta tem quantos quiser, um deles
/// é o padrão, e cada Empresa (Empresa.ModeloEmailNotaId) ou Cliente
/// (Cliente.ModeloEmailNotaId) pode apontar para outro. Ordem no envio:
/// cliente → empresa → padrão da conta → texto padrão do sistema.
/// Assunto e mensagem são texto simples com campos entre colchetes, ex.:
/// "[Número da nota]" (ver RegrasModeloEmailNota).
/// </summary>
public sealed class ModeloEmailNota : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Como aparece nas listas, ex.: "Padrão escritório". Único na conta.</summary>
    public string Nome { get; set; } = string.Empty;

    /// <summary>O modelo de quem não escolheu nenhum. Um por conta.</summary>
    public bool Padrao { get; set; }

    public string Assunto { get; set; } = string.Empty;

    public string Mensagem { get; set; } = string.Empty;

    /// <summary>Reply-To. Nulo = e-mail da empresa que emitiu a nota.</summary>
    public string? ResponderPara { get; set; }

    /// <summary>Cópia oculta de todo envio (ex.: escritório que guarda tudo). Nulo = sem cópia.</summary>
    public string? CopiaOcultaPara { get; set; }

    /// <summary>Tira o convite do micoempresa do rodapé (só vale no plano Escritório).</summary>
    public bool OcultarDivulgacao { get; set; }

    public const int TamanhoMaximoNome = 60;
    public const int TamanhoMaximoAssunto = 150;
    public const int TamanhoMaximoMensagem = 4000;
    public const int TamanhoMaximoEmail = 256;
}
