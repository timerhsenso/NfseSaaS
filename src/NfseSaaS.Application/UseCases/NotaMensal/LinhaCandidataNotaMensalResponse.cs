namespace NfseSaaS.Application.UseCases.NotaMensal;

public sealed record LinhaCandidataNotaMensalResponse(
    Guid ServicoId,
    string ServicoDescricao,
    string CodigoTributacaoNacional,
    decimal Quantidade,
    decimal ValorUnitario,
    decimal ValorTotal);
