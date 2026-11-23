using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;
using TaskManagementSystem.Exceptions;

namespace TaskManagementSystem.Filters;

/// <summary>
/// Global MVC action filter: before an action runs, validates every action argument that has a
/// FluentValidation validator registered (IValidator&lt;T&gt;). On failure it throws
/// RequestValidationException → ExceptionHandlingMiddleware returns 400 ValidationProblemDetails.
///
/// Result: controllers and services can assume their input is valid — no validation code in actions.
/// (FluentValidation's old automatic ASP.NET integration package is deprecated; this filter is the
/// recommended replacement.)
/// </summary>
public sealed class ValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new Dictionary<string, List<string>>();

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator)
            {
                continue; // e.g. an int id or CancellationToken — nothing to validate
            }

            var result = await validator.ValidateAsync(
                new ValidationContext<object>(argument), context.HttpContext.RequestAborted);

            foreach (var failure in result.Errors)
            {
                // "Email" → "email" so keys match the JSON property names the client sent.
                var key = JsonNamingPolicy.CamelCase.ConvertName(failure.PropertyName);
                if (!errors.TryGetValue(key, out var messages))
                {
                    errors[key] = messages = [];
                }
                messages.Add(failure.ErrorMessage);
            }
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }

        await next();
    }
}
