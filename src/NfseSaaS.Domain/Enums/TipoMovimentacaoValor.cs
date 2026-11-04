namespace NfseSaaS.Domain.Enums;

/// <summary>
/// Por que o valor do Contrato mudou — cada evento do Histórico de valores
/// (MovimentacaoValorContrato) tem um destes tipos.
///
/// Valores começam em 1 de propósito: a coluna tem DEFAULT no banco
/// (Reajuste, pra converter as linhas da antiga reajustes_contrato), e um
/// enum com valor 0 coincidiria com o default do CLR — o EF omitiria o
/// campo no INSERT e o banco gravaria o default no lugar de ValorInicial.
/// </summary>
public enum TipoMovimentacaoValor
{
    /// <summary>Valor e composição com que o Contrato foi criado.</summary>
    ValorInicial = 1,

    /// <summary>Correção monetária (por índice ou percentual negociado).</summary>
    Reajuste = 2,

    /// <summary>Mudança de escopo/preço negociado (aditivo): serviços incluídos, removidos ou repactuados.</summary>
    Alteracao = 3
}
