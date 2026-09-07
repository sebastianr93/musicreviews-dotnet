using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MusicReviews.Api.Infrastructure;

/// <summary>
/// Ejecuta el <see cref="IValidator{T}"/> de FluentValidation que corresponda a cada
/// argumento de la accion antes de invocarla.
/// </summary>
/// <remarks>
/// FluentValidation quito su integracion automatica con el pipeline de MVC
/// (<c>AddFluentValidationAutoValidation</c> quedo deprecado), asi que el enganche
/// explicito es este filtro. La ventaja es que el error sale como ValidationProblemDetails,
/// el mismo formato que produce la validacion por DataAnnotations de MVC.
/// </remarks>
public sealed class ValidationFilter : IAsyncActionFilter
{
    private readonly IServiceProvider _services;

    public ValidationFilter(IServiceProvider services)
    {
        _services = services;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());

            if (_services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var validationContext = new ValidationContext<object>(argument);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);

            if (result.IsValid)
            {
                continue;
            }

            foreach (var failure in result.Errors)
            {
                context.ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(context.ModelState)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "La solicitud no es válida.",
                Instance = context.HttpContext.Request.Path
            });

            return;
        }

        await next();
    }
}
