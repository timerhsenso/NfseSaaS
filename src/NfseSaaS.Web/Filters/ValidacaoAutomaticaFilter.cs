using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;
using NfseSaaS.Application.Exceptions;

namespace NfseSaaS.Web.Filters;

/// <summary>
/// Roda antes de toda action da API: para cada argumento cujo tipo tenha um
/// IValidator&lt;T&gt; registrado (FluentValidation), valida automaticamente.
/// Se inválido, lança ValidacaoException — tratada pelo
/// ExceptionHandlingMiddleware, que devolve 422 com os erros por campo.
/// Controllers não precisam chamar validação manualmente em cada action.
/// </summary>
public sealed class ValidacaoAutomaticaFilter : IAsyncActionFilter
{
    private readonly IServiceProvider _serviceProvider;

    public ValidacaoAutomaticaFilter(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argumento in context.ActionArguments.Values)
        {
            if (argumento is null)
                continue;

            var validatorType = typeof(IValidator<>).MakeGenericType(argumento.GetType());

            if (_serviceProvider.GetService(validatorType) is not IValidator validator)
                continue;

            var validationContext = new ValidationContext<object>(argumento);
            var resultado = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);

            if (!resultado.IsValid)
            {
                var erros = resultado.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

                throw new ValidacaoException(erros);
            }
        }

        await next();
    }
}
