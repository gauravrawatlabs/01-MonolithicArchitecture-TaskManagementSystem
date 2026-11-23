using Microsoft.AspNetCore.Mvc;
using TaskManagementSystem.Exceptions;

namespace TaskManagementSystem.Middleware;

/// <summary>
/// Outermost middleware: catches every exception thrown further down the pipeline
/// (controllers, services, repositories, EF Core) and converts it into an RFC 9457
/// ProblemDetails JSON response. Controllers and services therefore never need try/catch
/// just to produce an error response.
///
/// Phase 4 version. Phase 11 extends it with FluentValidation errors, Serilog and correlation IDs.
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IHostEnvironment environment)
{
    // Non-standard status (popularised by nginx) for "client closed the connection".
    private const int ClientClosedRequest = 499;

    // Middleware is created ONCE (effectively a singleton), so scoped/transient services
    // must be requested as InvokeAsync parameters, not constructor parameters.
    // IProblemDetailsService is a singleton, but asking for it here shows the pattern.
    public async Task InvokeAsync(HttpContext context, IProblemDetailsService problemDetailsService)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client disconnected; nobody is waiting for a response. Not an error.
            logger.LogInformation("Request {Method} {Path} was cancelled by the client.",
                context.Request.Method, context.Request.Path);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = ClientClosedRequest;
            }
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            // If headers/body were already sent we cannot replace the response, so the filter
            // lets the exception propagate unchanged (original stack trace intact) and the
            // server aborts the connection and logs it.
            await HandleExceptionAsync(context, exception, problemDetailsService);
        }
    }

    private async Task HandleExceptionAsync(
        HttpContext context, Exception exception, IProblemDetailsService problemDetailsService)
    {
        var (statusCode, title, detail) = exception switch
        {
            // Expected errors: our own messages, safe to show.
            AppException appException => (appException.StatusCode, appException.Title, appException.Message),

            // Unexpected errors: full details only in Development. In Production the client gets a
            // generic message — stack traces, SQL, server names and connection info never leak.
            _ => (StatusCodes.Status500InternalServerError,
                  "An unexpected error occurred",
                  environment.IsDevelopment()
                      ? exception.ToString()
                      : "An unexpected error occurred. Please try again later.")
        };

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            // Full exception (with stack trace) goes to the LOG, never to the client.
            logger.LogError(exception, "Unhandled exception while processing {Method} {Path}",
                context.Request.Method, context.Request.Path);
        }
        else
        {
            logger.LogWarning("Request {Method} {Path} failed with {StatusCode}: {Message}",
                context.Request.Method, context.Request.Path, statusCode, exception.Message);
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = $"{context.Request.Method} {context.Request.Path}"
        };

        // Validation errors get an extra "errors" dictionary { "password": ["..."], ... } that clients
        // can show next to each field (the same JSON shape as ValidationProblemDetails).
        // NOTE: we add it as an extension instead of using ValidationProblemDetails, because
        // IProblemDetailsService serializes the declared ProblemDetails type and would silently
        // drop the derived class's Errors property.
        if (exception is RequestValidationException validationException)
        {
            problemDetails.Extensions["errors"] = validationException.Errors;
        }

        // IProblemDetailsService writes application/problem+json and adds "type" and "traceId".
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problemDetails,
            Exception = exception
        });
    }
}
