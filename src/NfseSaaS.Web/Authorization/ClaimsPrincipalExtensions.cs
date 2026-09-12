using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NfseSaaS.Infrastructure.Identity;

namespace NfseSaaS.Web.Authorization;

/// <summary>
/// Leitura da permissão IAEC (ver Grupo/GrupoTela) direto dos Claims do
/// usuário logado — mesmo Claim que ApplicationUserClaimsPrincipalFactory
/// grava no login. Usado nas Views Razor (ex.: @if
/// (User.PodeAlterar(TelaCatalogo.Clientes))) pra habilitar/desabilitar
/// botão sem chamada extra de API, e em qualquer lugar que precise da
/// checagem fora do escopo de uma action inteira (ver
/// RequerPermissaoAttribute pra esse caso mais comum).
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static bool PodeIncluir(this ClaimsPrincipal user, string tela) => TemLetra(user, tela, 'I');

    public static bool PodeAlterar(this ClaimsPrincipal user, string tela) => TemLetra(user, tela, 'A');

    public static bool PodeExcluir(this ClaimsPrincipal user, string tela) => TemLetra(user, tela, 'E');

    public static bool PodeConsultar(this ClaimsPrincipal user, string tela) => TemLetra(user, tela, 'C');

    /// <summary>True se o usuário tiver QUALQUER acesso à tela (ao menos uma letra) — útil pra decidir se o item de menu aparece.</summary>
    public static bool TemAcesso(this ClaimsPrincipal user, string tela) =>
        !string.IsNullOrEmpty(user.FindFirstValue(ApplicationUserClaimsPrincipalFactory.PermissaoClaimTypePrefix + tela));

    private static bool TemLetra(ClaimsPrincipal user, string tela, char letra)
    {
        var valor = user.FindFirstValue(ApplicationUserClaimsPrincipalFactory.PermissaoClaimTypePrefix + tela);
        return valor is not null && valor.Contains(letra);
    }
}
