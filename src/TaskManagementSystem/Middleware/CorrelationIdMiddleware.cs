using Serilog.Context;

namespace TaskManagementSystem.Middleware;

/// <summary>
/// Gives every request a correlation id — the single value that ties together the client's error
/// message, the response header and ALL log lines written while handling that request.
///
/// - Reuses a valid incoming X-Correlation-ID (e.g. set by a front-end, gateway or calling service),
///   otherwise generates one.
/// - Echoes it in the X-Correlation-ID response header.
/// - Pushes it into Serilog's LogContext, so every log event in this request carries "CorrelationId".
///
/// Must be the OUTERMOST middleware, so even the exception handler's logs are inside the scope.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string HttpContextItemKey = "CorrelationId";
    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrCreateCorrelationId(context);
        context.Items[HttpContextItemKey] = correlationId;

        // Headers can only be set before the response starts; OnStarting runs at exactly that moment.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        return IsSafe(incoming) ? incoming : Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Incoming values are attacker-controlled and end up in logs and response headers.
    /// Only short, plain values are accepted — this blocks LOG INJECTION (e.g. "\r\nFAKE LOG LINE")
    /// and header-injection attempts. Anything else is replaced by a fresh id.
    /// </summary>
    private static bool IsSafe(string value) =>
        value.Length is > 0 and <= MaxLength &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
