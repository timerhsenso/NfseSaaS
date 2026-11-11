namespace NfseSaaS.Application.UseCases.NotaMensal;

/// <summary>Uma linha (serviço) do Contrato como a Nota Mensal enxerga.</summary>
public sealed record LinhaContratoValor(Guid ContratoServicoId, string Descricao, decimal ValorContrato, bool ValorVariavel);

/// <summary>Valor decidido para uma linha nesta emissão.</summary>
/// <param name="GravarNoContrato">Linha fixa cujo valor novo vira o valor do contrato.</param>
public sealed record ValorDecidido(Guid ContratoServicoId, decimal Valor, bool GravarNoContrato);

/// <summary>
/// Regras puras do valor de cada serviço na Nota Mensal:
/// - variável (ValorVariavel): pode mudar sempre; sugestão = último valor
///   emitido; nunca grava no contrato (mudar todo mês bagunçaria o
///   Histórico de valores e a base do reajuste);
/// - fixo: vem do contrato; mudar só nesta nota exige o contrato permitir
///   (PermitirAlterarValorNaEmissao); "gravar no contrato" sempre pode
///   (vira Alteração no Histórico de valores) — a permissão de editar
///   contratos é conferida na rota.
/// </summary>
public static class RegrasValorNotaMensal
{
    public static decimal Sugestao(decimal valorContrato, bool variavel, decimal? ultimoValor) =>
        variavel && ultimoValor is > 0 ? ultimoValor.Value : valorContrato;

    /// <summary>Lança <see cref="InvalidOperationException"/> com a mensagem para o usuário quando não pode.</summary>
    public static IReadOnlyList<ValorDecidido> Decidir(
        IReadOnlyList<LinhaContratoValor> linhas, IReadOnlyList<ValorServicoNotaMensalRequest>? pedidos, bool permiteAlterarNaEmissao)
    {
        var porLinha = (pedidos ?? Array.Empty<ValorServicoNotaMensalRequest>())
            .GroupBy(p => p.ContratoServicoId)
            .ToDictionary(g => g.Key, g => g.Last());

        var desconhecida = porLinha.Keys.FirstOrDefault(id => linhas.All(l => l.ContratoServicoId != id));
        if (desconhecida != Guid.Empty)
            throw new InvalidOperationException("Um dos serviços informados não é mais deste contrato. Busque os contratos de novo.");

        var decididos = new List<ValorDecidido>();
        foreach (var linha in linhas)
        {
            if (!porLinha.TryGetValue(linha.ContratoServicoId, out var pedido))
            {
                decididos.Add(new ValorDecidido(linha.ContratoServicoId, linha.ValorContrato, false));
                continue;
            }

            if (pedido.Valor <= 0)
                throw new InvalidOperationException($"O valor de \"{linha.Descricao}\" precisa ser maior que zero.");

            if (linha.ValorVariavel || pedido.Valor == linha.ValorContrato)
            {
                decididos.Add(new ValorDecidido(linha.ContratoServicoId, pedido.Valor, false));
                continue;
            }

            if (!pedido.GravarNoContrato && !permiteAlterarNaEmissao)
                throw new InvalidOperationException(
                    $"O contrato não permite mudar o valor de \"{linha.Descricao}\" só nesta nota: marque \"gravar no contrato\" ou marque o serviço como valor variável.");

            decididos.Add(new ValorDecidido(linha.ContratoServicoId, pedido.Valor, pedido.GravarNoContrato));
        }
        return decididos;
    }

    /// <summary>
    /// Quantidade e valor unitário que dão exatamente o novo total da linha:
    /// mantém a quantidade se a divisão fecha em centavos; senão, 1 × total.
    /// </summary>
    public static (decimal Quantidade, decimal ValorUnitario) RecomporLinha(decimal quantidadeAtual, decimal novoTotal)
    {
        if (quantidadeAtual > 0)
        {
            var unitario = Math.Round(novoTotal / quantidadeAtual, 2);
            if (unitario * quantidadeAtual == novoTotal)
                return (quantidadeAtual, unitario);
        }
        return (1m, novoTotal);
    }
}
