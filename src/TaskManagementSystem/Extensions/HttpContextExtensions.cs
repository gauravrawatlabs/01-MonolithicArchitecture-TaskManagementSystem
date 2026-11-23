using TaskManagementSystem.Middleware;

namespace TaskManagementSystem.Extensions;

public static class HttpContextExtensions
{
    /// <summary>
    /// The client's IP address, recorded on refresh tokens and in request logs for auditing.
    /// Behind a reverse proxy / load balancer this is the proxy's IP unless the
    /// Forwarded Headers middleware is configured (production concern).
    /// </summary>
    public static string? GetClientIpAddress(this HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>The correlation id assigned by <see cref="CorrelationIdMiddleware"/>, or null outside a request.</summary>
    public static string? GetCorrelationId(this HttpContext httpContext) =>
        httpContext.Items.TryGetValue(CorrelationIdMiddleware.HttpContextItemKey, out var value) ? value as string : null;
}
