using NfseSaaS.Domain.Common;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Representa um cliente do SaaS (a assinatura/conta). Entidade GLOBAL:
/// NÃO implementa ITenantEntity e NÃO sofre o Global Query Filter de tenant
/// — do contrário seria impossível localizar o próprio tenant antes de
/// existir um TenantId "atual" resolvido.
/// </summary>
public sealed class Tenant : BaseEntity
{
    /// <summary>
    /// Documento do titular da conta: CNPJ (14, pode ter letras no CNPJ
    /// alfanumérico) ou CPF (11), só caracteres, sem máscara. Único: uma conta
    /// por documento. Não é o CNPJ que emite (esse é o de cada Empresa).
    /// </summary>
    public string Documento { get; set; } = string.Empty;

    /// <summary>Empresa (emite as próprias notas) ou Contador (emite pelos clientes).</summary>
    public PerfilConta Perfil { get; set; } = PerfilConta.Empresa;

    /// <summary>Nome do titular: razão social (CNPJ) ou nome da pessoa (CPF).</summary>
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

    /// <summary>
    /// Paleta de cores da conta (Aparência), escolhida pelo administrador e
    /// válida para todos os usuários. Código do AparenciaCatalogo; nulo = padrão.
    /// </summary>
    public string? PaletaCores { get; set; }

    /// <summary>
    /// WhatsApp do dono da conta, para o micoempresa falar com ele (suporte,
    /// cobrança) e, mais adiante, mandar avisos (notas, reajustes, código da
    /// verificação em duas etapas). Celular do Brasil no formato
    /// internacional sem "+": 55 + DDD + 9 dígitos (13 caracteres). Opcional.
    /// </summary>
    public string? WhatsApp { get; set; }

    /// <summary>
    /// Quando o dono autorizou contato e avisos por WhatsApp (LGPD: só manda
    /// mensagem quem autorizou). Nulo = não autorizou (o número pode existir,
    /// ex.: informado para suporte, mas sem aviso automático).
    /// </summary>
    public DateTimeOffset? WhatsAppAutorizadoEm { get; set; }

    /// <summary>"Agora não" na faixa que pede o WhatsApp: não mostra até esta data.</summary>
    public DateTimeOffset? LembreteWhatsAppAdiadoAte { get; set; }

    /// <summary>
    /// Encerramento da conta (D2): a pedido do cliente, pela plataforma ou
    /// automático. Encerrada também fica com Ativo=false (ninguém entra), mas,
    /// diferente da suspensão, tem data para os dados serem apagados.
    /// </summary>
    public DateTimeOffset? EncerradaEm { get; set; }

    public OrigemEncerramento? EncerramentoOrigem { get; set; }

    /// <summary>
    /// Dia (Brasília) a partir do qual o job apaga os dados: sem notas, a
    /// conta inteira; com notas, tudo menos os documentos fiscais, que ficam
    /// pelo prazo legal. Até lá o operador pode reabrir a conta.
    /// </summary>
    public DateOnly? ExpurgoPrevistoEm { get; set; }

    /// <summary>Quando os dados não fiscais foram apagados (a conta não pode mais ser reaberta).</summary>
    public DateTimeOffset? DadosExpurgadosEm { get; set; }

    /// <summary>
    /// Desde quando o administrador exige a verificação em duas etapas de
    /// TODOS os usuários da conta (SEG1). Nulo = só o que a plataforma já
    /// exige (administradores e operador). Quem ainda não ligou tem
    /// RegrasDuploFator.DiasPrazoAdesao dias a partir desta data.
    /// </summary>
    public DateTimeOffset? DuploFatorObrigatorioDesde { get; set; }
}
