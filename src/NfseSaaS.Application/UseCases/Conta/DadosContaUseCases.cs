using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Conta;

/// <summary>Seção "Dados da conta" (Configurações da conta) e faixa que pede o WhatsApp.</summary>
/// <param name="Nome">Razão social ou nome do titular (como no cadastro).</param>
/// <param name="EmailResponsavel">E-mail de quem criou a conta pelo cadastro (não convidado).</param>
/// <param name="WhatsApp">Formatado para a tela, ex.: "(71) 99999-8888"; nulo = não informado.</param>
/// <param name="PodeAlterar">Administrador da conta (grupo administrador).</param>
/// <param name="MostrarLembreteWhatsApp">Faixa do painel inicial: administrador, sem WhatsApp e sem "agora não" vigente.</param>
public sealed record DadosContaResponse(
    string Nome,
    string Documento,
    PerfilConta Perfil,
    string? EmailResponsavel,
    string? WhatsApp,
    bool WhatsAppAutorizado,
    bool PodeAlterar,
    bool MostrarLembreteWhatsApp);

/// <param name="WhatsApp">Com ou sem máscara; vazio = remover.</param>
/// <param name="AutorizaContato">Aceita contato e avisos do micoempresa por WhatsApp (LGPD).</param>
public sealed record SalvarContatoContaRequest(string? WhatsApp, bool AutorizaContato);

public interface IDadosContaUseCase
{
    Task<DadosContaResponse> ObterAsync(CancellationToken cancellationToken);

    /// <summary>Só administrador da conta.</summary>
    Task<DadosContaResponse> SalvarContatoAsync(SalvarContatoContaRequest request, CancellationToken cancellationToken);

    /// <summary>"Agora não" na faixa do WhatsApp: some por <see cref="DiasAdiarLembrete"/> dias.</summary>
    Task AdiarLembreteWhatsAppAsync(CancellationToken cancellationToken);

    public const int DiasAdiarLembrete = 30;
}
