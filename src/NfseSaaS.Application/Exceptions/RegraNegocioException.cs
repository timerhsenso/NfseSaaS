namespace NfseSaaS.Application.Exceptions;

/// <summary>Lançada quando uma requisição viola uma regra de negócio (ex.: duplicidade, estado inválido) — situação previsível, não um erro de sistema.</summary>
public sealed class RegraNegocioException : Exception
{
    public RegraNegocioException(string mensagem) : base(mensagem) { }
}
