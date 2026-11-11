namespace NfseSaaS.Application.UseCases.NotaMensal;

public sealed record LinhaCandidataNotaMensalResponse(
    Guid ServicoId,
    string ServicoDescricao,
    string CodigoTributacaoNacional,
    decimal Quantidade,
    decimal ValorUnitario,
    decimal ValorTotal,
    // Linha do Contrato (para mandar o valor de volta na emissão).
    Guid ContratoServicoId = default,
    bool ValorVariavel = false,
    // Valor deste serviço na última nota autorizada do contrato antes da
    // competência (nulo = ainda não há) e a competência dela.
    decimal? UltimoValor = null,
    DateOnly? UltimaCompetencia = null,
    // O que a tela já preenche: variável → último valor (ou o do contrato); fixo → contrato.
    decimal ValorSugerido = 0);
