using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Web.Services;

namespace NfseSaaS.Web.Filters;

/// <summary>
/// Operação sensível (SEG1): sem senha digitada nos últimos
/// <see cref="ConfirmacaoSenhaRecente.Minutos"/> minutos, devolve 403 com
/// { erro, codigo: "confirmar_senha" }. O apiFetch e o envio de arquivo
/// (site.js) abrem a janela "Confirme a sua senha", chamam
/// POST api/auth/confirmar-senha e repetem a operação sozinhos.
/// Roda depois da autorização: quem não tem permissão recebe o 403 normal.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequerConfirmacaoSenhaAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var servicos = context.HttpContext.RequestServices;
        var usuarioId = servicos.GetRequiredService<ICurrentUser>().UserId;
        if (usuarioId is { } id && servicos.GetRequiredService<IConfirmacaoSenhaRecente>().EstaValida(context.HttpContext, id))
        {
            await next();
            return;
        }

        context.Result = new ObjectResult(new
        {
            erro = ConfirmacaoSenhaRecente.MensagemExigida,
            codigo = ConfirmacaoSenhaRecente.CodigoExigida
        })
        { StatusCode = StatusCodes.Status403Forbidden };
    }
}
