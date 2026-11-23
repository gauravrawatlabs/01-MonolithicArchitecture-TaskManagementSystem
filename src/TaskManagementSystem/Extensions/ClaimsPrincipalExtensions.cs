using System.Security.Claims;
using TaskManagementSystem.Constants;
using TaskManagementSystem.Exceptions;

namespace TaskManagementSystem.Extensions;

/// <summary>Helpers for reading our claims from HttpContext.User (populated by the JwtBearer middleware).</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>The current user's id from the "sub" claim.</summary>
    /// <exception cref="UnauthorizedException">The claim is missing or not a Guid.</exception>
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        principal.TryGetUserId(out var userId)
            ? userId
            : throw new UnauthorizedException("The access token does not contain a valid user id.");

    /// <summary>Non-throwing variant, for authorization handlers (a missing id simply means "not allowed").</summary>
    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(AppClaimTypes.Subject), out userId);

    public static bool IsAdmin(this ClaimsPrincipal principal) => principal.IsInRole(Roles.Admin);
}
