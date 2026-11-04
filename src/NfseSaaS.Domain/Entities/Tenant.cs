using NfseSaaS.Domain.Common;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Representa um cliente do SaaS (a assinatura/conta). Entidade GLOBAL:
/// NÃO implementa ITenantEntity e NÃO sofre o Global Query Filter de tenant
/// — do contrário seria impossível localizar o próprio tenant antes de
/// existir um TenantId "atual" resolvido.
/// </summary>
public sealed class Tenant : BaseEntity
{
    public string Cnpj { get; set; } = string.Empty;

    public string RazaoSocial { get; set; } = string.Empty;

    public string? NomeFantasia { get; set; }

    /// <summary>
    /// false = conta suspensa pelo operador da plataforma (painel
    /// Plataforma): ninguém do tenant entra, as sessões abertas caem em até
    /// 1 minuto e nenhuma nota é emitida (nem pela automação).
    /// </summary>
    public bool Ativo { get; set; } = true;

    /// <summary>Quando a conta foi suspensa (nulo se ativa).</summary>
    public DateTimeOffset? BloqueadoEm { get; set; }

    /// <summary>Por que foi suspensa (ex.: inadimplência, pedido do cliente) — só o operador vê.</summary>
    public string? MotivoBloqueio { get; set; }

    /// <summary>
    /// Código do link "Indique e ganhe" desta conta (ex.: micoempresa.com.br/?ref=K7M2QX9A).
    /// Gerado no cadastro; contas antigas ganham o código na primeira visita à tela de indicação.
    /// </summary>
    public string? CodigoIndicacao { get; set; }

    /// <summary>Conta que indicou esta (veio pelo link de indicação dela no cadastro).</summary>
    public Guid? IndicadoPorTenantId { get; set; }

    /// <summary>
    /// De onde veio o cadastro (ex.: "indicacao", "email-nota"), lido do endereço
    /// que trouxe a pessoa ao site. Só para o operador medir os canais.
    /// </summary>
    public string? OrigemCadastro { get; set; }
}
