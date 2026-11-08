namespace NfseSaaS.Application.UseCases.Servicos;

public sealed record AtualizarServicoRequest(
    string Descricao,
    string CodigoTributacaoNacional,
    string CodigoNbs,
    decimal ValorPadrao,
    string? CIndOp = null,
    string? CClassTrib = null);
