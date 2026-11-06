using System.Globalization;
using NfseSaaS.Application.Exceptions;

namespace NfseSaaS.Application.UseCases.Cobranca;

/// <summary>Preço e condição de fundador que a assinatura passa a ter no novo plano.</summary>
public sealed record TrocaPlanoCalculada(decimal Preco, bool Fundador);

/// <summary>
/// Regras da troca de plano pela tela Assinatura, sem banco nem gateway:
/// - não troca para o mesmo plano;
/// - não desce para um plano com menos empresas do que as ativas na conta
///   (o limite de notas é suave e não impede a troca);
/// - fundador no MESMO grupo (empresas ou escritórios) continua fundador,
///   com o preço de fundador do novo plano;
/// - ao mudar de grupo, vale a regra de quem assina hoje: preço de fundador
///   só se ainda houver vaga no grupo novo.
/// O novo valor vale a partir da próxima fatura (ver CobrancaService).
/// </summary>
public static class RegrasTrocaPlano
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static TrocaPlanoCalculada Calcular(
        PlanoVigente? atual,
        bool fundadorAtual,
        PlanoVigente novo,
        int vagasFundadorNoGrupoNovo,
        int empresasAtivas)
    {
        if (atual is not null && atual.Codigo == novo.Codigo)
            throw new RegraNegocioException($"A conta já está no plano {novo.Nome}.");

        if (empresasAtivas > novo.MaxEmpresas)
            throw new RegraNegocioException(
                $"Esta conta tem {empresasAtivas} empresa(s) ativa(s) e o plano {novo.Nome} permite até {novo.MaxEmpresas}. Desative as empresas que não usa (em Empresas) antes de trocar.");

        return Preco(atual, fundadorAtual, novo, vagasFundadorNoGrupoNovo);
    }

    /// <summary>Mesmo cálculo de preço de <see cref="Calcular"/>, sem as validações (para mostrar na tela).</summary>
    public static TrocaPlanoCalculada Preco(PlanoVigente? atual, bool fundadorAtual, PlanoVigente novo, int vagasFundadorNoGrupoNovo)
    {
        var fundador = atual is not null && atual.Publico == novo.Publico
            ? fundadorAtual
            : vagasFundadorNoGrupoNovo > 0;

        return new TrocaPlanoCalculada(fundador ? novo.PrecoFundador : novo.PrecoMensal, fundador);
    }

    /// <summary>Frase para a tela e a auditoria: "Mico (R$ 19,00) → Empresa (R$ 39,00)".</summary>
    public static string Descrever(string? planoAnterior, decimal? precoAnterior, string planoNovo, decimal precoNovo) =>
        $"{planoAnterior ?? "—"} ({(precoAnterior ?? 0m).ToString("C", PtBr)}) → {planoNovo} ({precoNovo.ToString("C", PtBr)})";
}
