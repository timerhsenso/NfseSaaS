using NfseSaaS.Application.UseCases.Onboarding;
using Xunit;

namespace NfseSaaS.Tests;

public class ProgressoOnboardingResponseTests
{
    private static PassoOnboarding Passo(string chave, bool concluido) =>
        new(chave, chave, chave, concluido, "/", "Ir", null);

    [Fact]
    public void Proximo_e_o_primeiro_passo_pendente_na_ordem()
    {
        var progresso = new ProgressoOnboardingResponse(new[]
        {
            Passo("empresa", true), Passo("certificado", false), Passo("servico", true), Passo("cliente", false)
        });

        Assert.Equal("certificado", progresso.Proximo?.Chave);
        Assert.Equal(2, progresso.Concluidos);
        Assert.False(progresso.Completo);
    }

    [Fact]
    public void Com_tudo_concluido_nao_ha_proximo()
    {
        var progresso = new ProgressoOnboardingResponse(new[] { Passo("empresa", true), Passo("producao", true) });

        Assert.True(progresso.Completo);
        Assert.Null(progresso.Proximo);
    }
}
