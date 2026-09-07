namespace NfseSaaS.Application.Exceptions;

/// <summary>
/// Lançada pelo pipeline de validação automática (FluentValidation) quando
/// um DTO de entrada é inválido. Mapeada para 422 pelo
/// ExceptionHandlingMiddleware, que inclui os erros por campo na resposta.
/// </summary>
public sealed class ValidacaoException : Exception
{
    public IReadOnlyDictionary<string, string[]> Erros { get; }

    public ValidacaoException(IReadOnlyDictionary<string, string[]> erros)
        : base("Um ou mais campos da requisição são inválidos.")
    {
        Erros = erros;
    }
}
