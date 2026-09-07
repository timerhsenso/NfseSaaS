namespace NfseSaaS.Application.UseCases.Servicos;

public sealed record ServicoResponse(
    Guid Id,
    Guid EmpresaId,
    string Descricao,
    string CodigoTributacaoNacional,
    string CodigoNbs,
    decimal ValorPadrao,
    bool Ativo,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
