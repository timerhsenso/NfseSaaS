namespace NfseSaaS.Web.Filters;

/// <summary>
/// Libera a action/controller do <see cref="ExigeAceiteTermosFilter"/>:
/// telas e rotas que precisam funcionar ANTES do usuário aceitar a versão
/// vigente dos termos — o próprio aceite e os textos (Legal), login,
/// cadastro, convite e senha (Account), sair (api/auth/logout) e a página
/// de erro.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class PermitirSemAceiteTermosAttribute : Attribute
{
}
