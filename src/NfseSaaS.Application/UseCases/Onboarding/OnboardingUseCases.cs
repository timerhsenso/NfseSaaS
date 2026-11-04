namespace NfseSaaS.Application.UseCases.Onboarding;

/// <summary>Um passo do "Primeiros passos" do painel.</summary>
/// <param name="Chave">Identificador estável do passo (empresa, certificado, servico, cliente, nota-teste, producao).</param>
/// <param name="Titulo">O que fazer, em poucas palavras.</param>
/// <param name="Descricao">Por que e como, em uma frase.</param>
/// <param name="Link">Tela onde se faz o passo.</param>
/// <param name="TextoBotao">Rótulo do botão que leva à tela.</param>
/// <param name="LinkAjuda">Página de ajuda do passo, quando houver.</param>
public sealed record PassoOnboarding(
    string Chave,
    string Titulo,
    string Descricao,
    bool Concluido,
    string Link,
    string TextoBotao,
    string? LinkAjuda);

/// <summary>Progresso da conta até a primeira nota em produção.</summary>
public sealed record ProgressoOnboardingResponse(IReadOnlyList<PassoOnboarding> Passos)
{
    public int Concluidos => Passos.Count(p => p.Concluido);
    public bool Completo => Passos.All(p => p.Concluido);

    /// <summary>Primeiro passo pendente (o que o painel destaca), ou null quando completo.</summary>
    public PassoOnboarding? Proximo => Passos.FirstOrDefault(p => !p.Concluido);
}

/// <summary>Calcula o "Primeiros passos" da conta atual a partir do que já existe nela.</summary>
public interface IObterProgressoOnboardingUseCase
{
    Task<ProgressoOnboardingResponse> ExecutarAsync(CancellationToken cancellationToken);
}
