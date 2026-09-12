using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Identity;

namespace NfseSaaS.Web.Filters;

/// <summary>
/// Sucessor de [Authorize(Roles = ...)] no módulo de segurança IAEC — em
/// vez de checar uma Role fixa do Identity, checa se o Claim de
/// permissão da Tela (gravado no login, ver
/// ApplicationUserClaimsPrincipalFactory) contém a letra da Ação pedida.
/// Sem chamada ao banco: lê só do Claims já carregado na requisição.
///
/// Único mecanismo de autorização de tela do sistema — todos os
/// controllers já migraram de [Authorize(Roles = ...)]; a Role antiga
/// do Identity não é mais lida nem escrita por nenhum código do projeto.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequerPermissaoAttribute : Attribute, IAuthorizationFilter
{
    private readonly string _tela;
    private readonly char _letraAcao;

    public RequerPermissaoAttribute(string tela, AcaoPermissao acao)
    {
        _tela = tela;
        _letraAcao = acao switch
        {
            AcaoPermissao.Incluir => 'I',
            AcaoPermissao.Alterar => 'A',
            AcaoPermissao.Excluir => 'E',
            AcaoPermissao.Consultar => 'C',
            _ => throw new ArgumentOutOfRangeException(nameof(acao), acao, null)
        };
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var valor = user.FindFirstValue(ApplicationUserClaimsPrincipalFactory.PermissaoClaimTypePrefix + _tela);

        if (valor is null || !valor.Contains(_letraAcao))
            context.Result = new ForbidResult();
    }
}
