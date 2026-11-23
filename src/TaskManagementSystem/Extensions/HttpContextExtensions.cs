namespace TaskManagementSystem.Extensions;

public static class HttpContextExtensions
{
    /// <summary>
    /// The client's IP address, recorded on refresh tokens for auditing.
    /// Behind a reverse proxy / load balancer this is the proxy's IP unless the
    /// Forwarded Headers middleware is configured (production concern).
    /// </summary>
    public static string? GetClientIpAddress(this HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString();
}
