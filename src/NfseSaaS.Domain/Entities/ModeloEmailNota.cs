using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Texto do e-mail que leva a nota (PDF e XML) ao cliente — Configurações
/// da conta › E-mail da nota. EmpresaId nulo = modelo da conta (vale para
/// todas as empresas); preenchido = modelo personalizado daquela empresa
/// (escritório com várias empresas). Sem linha nenhuma = texto padrão do
/// sistema. Assunto e mensagem são texto simples com campos entre
/// colchetes, ex.: "[Número da nota]" (ver RegrasModeloEmailNota).
/// </summary>
public sealed class ModeloEmailNota : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Nulo = modelo da conta; preenchido = modelo só desta empresa.</summary>
    public Guid? EmpresaId { get; set; }

    public string Assunto { get; set; } = string.Empty;

    public string Mensagem { get; set; } = string.Empty;

    /// <summary>Reply-To. Nulo = e-mail da empresa que emitiu a nota.</summary>
    public string? ResponderPara { get; set; }

    /// <summary>Cópia oculta de todo envio (ex.: escritório que guarda tudo). Nulo = sem cópia.</summary>
    public string? CopiaOcultaPara { get; set; }

    /// <summary>Tira o convite do micoempresa do rodapé (plano Escritório). Só no modelo da conta.</summary>
    public bool OcultarDivulgacao { get; set; }

    public const int TamanhoMaximoAssunto = 150;
    public const int TamanhoMaximoMensagem = 4000;
    public const int TamanhoMaximoEmail = 256;
}
