using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Um plano de assinatura (Plataforma › Planos). Entidade GLOBAL. O preço não
/// fica aqui: fica em <see cref="PrecoPlano"/>, com vigência e histórico.
/// Os limites são "suaves": servem para avisar e orientar a troca de plano,
/// nunca para travar uma emissão no meio do mês.
/// </summary>
public sealed class Plano : BaseEntity
{
    /// <summary>Código estável, gravado na Assinatura (nunca muda depois de criado).</summary>
    public int Codigo { get; set; }

    public string Nome { get; set; } = string.Empty;

    public string Descricao { get; set; } = string.Empty;

    /// <summary>Grupo das vagas de fundador (empresas ou escritórios).</summary>
    public PublicoPlano Publico { get; set; } = PublicoPlano.Empresas;

    /// <summary>Aparece para contas perfil Empresa.</summary>
    public bool OfertarParaEmpresa { get; set; }

    /// <summary>Aparece para contas perfil Contador.</summary>
    public bool OfertarParaContador { get; set; }

    /// <summary>Empresas ativas incluídas (cadastradas e não desativadas).</summary>
    public int MaxEmpresas { get; set; }

    /// <summary>Notas em produção por mês incluídas; nulo = sem limite de notas.</summary>
    public int? NotasPorMes { get; set; }

    /// <summary>Fora de venda: some da tela Assinatura; quem já assina continua no plano.</summary>
    public bool AVenda { get; set; } = true;

    /// <summary>Ordem de exibição (menor primeiro).</summary>
    public int Ordem { get; set; }
}

/// <summary>
/// Preço de um plano a partir de uma data. Nunca é sobrescrito por um preço
/// novo: cada mudança é uma linha nova, e o histórico fica completo. Vale o de
/// maior <see cref="VigenteDesde"/> até hoje. Quem já assina mantém o preço
/// contratado (Assinatura.PrecoMensal): o novo vale para novas assinaturas.
/// </summary>
public sealed class PrecoPlano : BaseEntity
{
    public Guid PlanoId { get; set; }

    public decimal PrecoMensal { get; set; }

    /// <summary>Preço para quem assina enquanto houver vaga de fundador no grupo do plano.</summary>
    public decimal PrecoFundador { get; set; }

    public DateOnly VigenteDesde { get; set; }

    /// <summary>Por que mudou (ex.: "reajuste anual", "campanha de lançamento").</summary>
    public string? Motivo { get; set; }

    /// <summary>Operador que cadastrou (nulo nos preços iniciais).</summary>
    public Guid? AlteradoPorUsuarioId { get; set; }
}

/// <summary>
/// Quantas assinaturas de cada grupo ganham o preço de fundador (preso
/// enquanto a assinatura existir). Editável em Plataforma › Planos; cada
/// mudança fica no Histórico do operador.
/// </summary>
public sealed class VagasFundador
{
    public PublicoPlano Publico { get; set; }

    public int Quantidade { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
