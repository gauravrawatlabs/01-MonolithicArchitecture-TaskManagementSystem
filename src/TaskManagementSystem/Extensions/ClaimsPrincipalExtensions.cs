using System.Security.Claims;
using TaskManagementSystem.Constants;
using TaskManagementSystem.Exceptions;

namespace TaskManagementSystem.Extensions;

/// <summary>Helpers for reading our claims from HttpContext.User (populated by the JwtBearer middleware).</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>The current user's id from the "sub" claim.</summary>
    /// <exception cref="UnauthorizedException">The claim is missing or not a Guid.</exception>
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(AppClaimTypes.Subject);
        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new UnauthorizedException("The access token does not contain a valid user id.");
    }
}
