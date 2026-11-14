namespace NfseSaaS.Web.Filters;

/// <summary>
/// Libera a action/controller do <see cref="ExigeDuploFatorFilter"/>: o que
/// precisa funcionar enquanto o usuário ainda não ligou a verificação em
/// duas etapas obrigatória — a própria tela de ligar e a API dela. O que já
/// é liberado dos Termos (<see cref="PermitirSemAceiteTermosAttribute"/>:
/// login, sair, textos legais, erro) também passa.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class PermitirSemDuploFatorAttribute : Attribute
{
}
