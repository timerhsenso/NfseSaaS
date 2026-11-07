using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Common;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Assinatura de uma conta (Tenant) no micoempresa. Uma por conta, criada na
/// primeira vez que a cobrança é consultada. Entidade GLOBAL (como o Tenant):
/// o webhook do Asaas chega sem usuário logado e precisa achá-la pelo id da
/// assinatura, então o acesso é sempre explícito por TenantId.
/// </summary>
public sealed class Assinatura : BaseEntity
{
    public Guid TenantId { get; set; }

    public StatusAssinatura Status { get; set; } = StatusAssinatura.Teste;

    /// <summary>Código do plano escolhido ao assinar (Plano.Codigo; nulo enquanto em teste).</summary>
    public int? Plano { get; set; }

    /// <summary>Valor mensal contratado (preço de fundador fica gravado aqui).</summary>
    public decimal? PrecoMensal { get; set; }

    public bool Fundador { get; set; }

    /// <summary>
    /// Vaga de fundador guardada para esta conta no cadastro (e-mail
    /// confirmado durante a campanha). Nula = sem reserva. É consumida (e
    /// limpa) quando a conta assina. Ver RegrasFundador.ReservaValida.
    /// </summary>
    public DateTimeOffset? FundadorReservadoEm { get; set; }

    /// <summary>Grupo de vagas da reserva (empresas ou escritórios, pelo perfil da conta).</summary>
    public PublicoPlano? FundadorReservaGrupo { get; set; }

    /// <summary>
    /// Reserva feita no beta (cobrança ainda desligada para a conta): vale
    /// sem prazo. As demais valem até 7 dias depois do fim do teste.
    /// </summary>
    public bool FundadorReservaSemPrazo { get; set; }

    /// <summary>
    /// Início do teste grátis em produção: a primeira nota emitida em
    /// produção. Nulo = ainda não usou produção (homologação não conta).
    /// </summary>
    public DateTimeOffset? TesteIniciadoEm { get; set; }

    public string? AsaasClienteId { get; set; }

    public string? AsaasAssinaturaId { get; set; }

    public DateTimeOffset? AssinadaEm { get; set; }

    /// <summary>
    /// Vencimento da primeira fatura de quem assinou durante o teste grátis
    /// (o dia em que o teste acaba). Enquanto não paga, a emissão segue até
    /// esse vencimento mais a tolerância de atraso.
    /// </summary>
    public DateOnly? PrimeiraCobrancaEm { get; set; }

    /// <summary>
    /// A conta não tem teste grátis: um CNPJ dela já usou o teste em outra
    /// conta (tabela testes_gratis_cnpj). Produção só com assinatura.
    /// </summary>
    public bool SemTesteGratis { get; set; }

    /// <summary>Dias de teste dados pelo operador (Plataforma › Contas), somados aos 30.</summary>
    public int TesteDiasExtras { get; set; }

    /// <summary>
    /// Isenta pelo operador (Plataforma › Contas), sem mexer na configuração
    /// da VPS. Vale até IsentaAte (inclusive); sem data = sem prazo. Só para
    /// conta sem assinatura ativa no Asaas (isenção não para faturas).
    /// </summary>
    public bool IsentaPelaPlataforma { get; set; }

    public DateOnly? IsentaAte { get; set; }

    public string? MotivoIsencao { get; set; }

    /// <summary>Vencimento da fatura em atraso (nulo quando em dia).</summary>
    public DateOnly? AtrasadaDesde { get; set; }

    public DateTimeOffset? UltimoPagamentoEm { get; set; }

    public DateTimeOffset? CanceladaEm { get; set; }

    /// <summary>
    /// Cancelada pela tela: a emissão em produção continua até este dia
    /// (fim do mês já pago ou do teste grátis). Nulo = bloqueia na hora.
    /// </summary>
    public DateOnly? AcessoAteAposCancelar { get; set; }

    /// <summary>Link da fatura em aberto (Pix, boleto ou cartão), para o botão "Pagar".</summary>
    public string? UrlFaturaPendente { get; set; }

    /// <summary>
    /// Vencimento da fatura em aberto (a do <see cref="UrlFaturaPendente"/>).
    /// Usado nos avisos "fatura criada", "vence amanhã" e "vencida" e na
    /// faixa do painel; nulo quando não há fatura em aberto.
    /// </summary>
    public DateOnly? VencimentoFaturaPendente { get; set; }

    /// <summary>Valor da fatura em aberto (nulo quando não há).</summary>
    public decimal? ValorFaturaPendente { get; set; }

    /// <summary>Esquece a fatura em aberto (paga, removida ou assinatura cancelada).</summary>
    public void LimparFaturaPendente()
    {
        UrlFaturaPendente = null;
        VencimentoFaturaPendente = null;
        ValorFaturaPendente = null;
    }
}
