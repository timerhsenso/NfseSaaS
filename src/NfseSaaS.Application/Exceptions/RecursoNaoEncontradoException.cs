namespace NfseSaaS.Application.Exceptions;

/// <summary>Lançada quando um caso de uso referencia um recurso (Empresa, Cliente, Serviço etc.) que não existe ou não pertence ao tenant atual.</summary>
public sealed class RecursoNaoEncontradoException : Exception
{
    public RecursoNaoEncontradoException(string mensagem) : base(mensagem) { }
}
