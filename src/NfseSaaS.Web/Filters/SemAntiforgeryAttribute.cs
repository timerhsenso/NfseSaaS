namespace NfseSaaS.Web.Filters;

/// <summary>
/// Dispensa o anti-CSRF (ValidacaoAntiforgeryFilter) numa action chamada
/// servidor-a-servidor, sem cookie de navegador — hoje só o webhook do
/// Asaas, que se autentica por token no header. Não usar em action chamada
/// pelo navegador do usuário.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SemAntiforgeryAttribute : Attribute
{
}
